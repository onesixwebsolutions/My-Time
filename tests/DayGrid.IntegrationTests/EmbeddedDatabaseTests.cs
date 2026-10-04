using DayGrid.Api;
using DayGrid.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests;

/// <summary>
/// Exercises the desktop exe's Database:Mode=Embedded path (EmbeddedDatabase.StartAsync) in a
/// throwaway data directory — never %LocalAppData%\DayGrid.
/// </summary>
[Collection(PostgresCollection.Name)] // serialised with the other suites; uses its own server
public class EmbeddedDatabaseTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(PostgresFixture.CacheRoot, "embedded-" + Guid.NewGuid().ToString("N"));

    public EmbeddedDatabaseTests()
    {
        // Reuse the fixture's downloaded binaries instead of fetching ~23 MB again.
        var cachedBinaries = Path.Combine(PostgresFixture.CacheRoot, "pg_embed", "binaries");
        var target = Path.Combine(_dataDir, "pg_embed", "binaries");
        Directory.CreateDirectory(target);
        foreach (var file in Directory.GetFiles(cachedBinaries))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }

    [Fact]
    public async Task FirstRun_CreatesSchemaOnFreePort_SurvivesLogFlood_AndRestartKeepsData()
    {
        var schema = PostgresFixture.InitSqlPath;

        var (server, cs) = await EmbeddedDatabase.StartAsync(_dataDir, schema);
        try
        {
            Assert.NotEqual(5432, server.PgPort);
            await using (var connection = new NpgsqlConnection(cs))
            {
                await connection.OpenAsync();
                await using (var insert = new NpgsqlCommand("INSERT INTO simple_tasks (title) VALUES ('persisted')", connection))
                    await insert.ExecuteNonQueryAsync();

                // Every failed statement is logged with its text. Before logging_collector=on this
                // went to an unread pipe and froze the server after a few hundred KB.
                var longTitle = new string('x', 4000);
                for (var i = 0; i < 300; i++)
                {
                    await using var failing = new NpgsqlCommand("INSERT INTO simple_tasks (title) VALUES (@t)", connection) { CommandTimeout = 15 };
                    failing.Parameters.AddWithValue("t", longTitle); // > varchar(200)
                    var ex = await Assert.ThrowsAsync<PostgresException>(() => failing.ExecuteNonQueryAsync());
                    Assert.Equal(PostgresErrorCodes.StringDataRightTruncation, ex.SqlState);
                }
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            server.Dispose();
        }

        var (restarted, cs2) = await EmbeddedDatabase.StartAsync(_dataDir, schema);
        try
        {
            await using var connection = new NpgsqlConnection(cs2);
            await connection.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM simple_tasks WHERE title = 'persisted'", connection);
            Assert.Equal(1L, await count.ExecuteScalarAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            restarted.Dispose();
        }
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 20 && Directory.Exists(_dataDir); attempt++)
        {
            try { Directory.Delete(_dataDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }
}
