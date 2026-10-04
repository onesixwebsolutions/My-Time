using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DayGrid.Api;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MysticMind.PostgresEmbed;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "Postgres";
}

/// <summary>
/// One real PostgreSQL 16 server for the whole test run: a fresh PgServer instance directory
/// (random instance id, deleted on stop) on a free ephemeral TCP port, a "daygrid_it" database
/// created through the real <see cref="SchemaBootstrapper"/> from db/init.sql, and a single
/// <see cref="IntegrationApiFactory"/> pointed at it. Only the downloaded Postgres binaries are
/// cached between runs (under %TEMP%\daygrid-integration-tests).
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string PgVersion = "16.2.0"; // keep in step with EmbeddedDatabase.PgVersion
    public const string DatabaseName = "daygrid_it";
    private const int ForbiddenPort = 5432; // the developer's own Postgres service lives here

    private PgServer? _server;
    private string? _appSettingsSeedSql;
    private string? _truncateSql;

    public static string CacheRoot { get; } = Path.Combine(Path.GetTempPath(), "daygrid-integration-tests");

    public int Port { get; private set; }
    public string AdminConnectionString { get; private set; } = string.Empty;
    public string ConnectionString { get; private set; } = string.Empty;
    public IntegrationApiFactory Factory { get; private set; } = null!;

    public static string RepoRoot { get; } = FindRepoRoot();
    public static string InitSqlPath => Path.Combine(RepoRoot, "db", "init.sql");
    public static string SeedSqlPath => Path.Combine(RepoRoot, "db", "seed.sql");

    /// <summary>Folder holding the embedded server's bin\ (initdb, pg_ctl, postgres, ...).</summary>
    public string? PgBinDir => _server?.PgBinDir;

    public async Task InitializeAsync()
    {
        CleanupStaleInstances();

        Port = GetFreeTcpPort();
        if (Port == ForbiddenPort)
            throw new InvalidOperationException("Refusing to start the test server on 5432.");

        _server = new PgServer(
            PgVersion,
            dbDir: CacheRoot,
            instanceId: Guid.NewGuid(),
            port: Port,
            pgServerParams: EmbeddedDatabase.ServerParameters(), // same settings as the desktop exe
            clearInstanceDirOnStop: true,
            clearWorkingDirOnStart: false,
            // Windows keeps the DLLs locked for a moment after pg_ctl stop returns; the default
            // retry budget (~0.5 s) is too short to delete the instance directory reliably.
            deleteFolderRetryCount: 8,
            deleteFolderInitialTimeout: 250,
            deleteFolderTimeoutFactor: 2);
        await _server.StartAsync();

        AdminConnectionString = BuildConnectionString("postgres");
        ConnectionString = BuildConnectionString(DatabaseName);

        await WaitUntilAcceptingConnectionsAsync(AdminConnectionString);
        await ExecuteAdminAsync($"CREATE DATABASE \"{DatabaseName}\"");

        var created = await SchemaBootstrapper.EnsureSchemaAsync(ConnectionString, InitSqlPath, NullLogger.Instance);
        if (!created)
            throw new InvalidOperationException("SchemaBootstrapper did not create the schema on a brand-new database.");

        var initSql = await File.ReadAllTextAsync(InitSqlPath);
        _appSettingsSeedSql = Regex.Match(initSql, @"INSERT INTO app_settings[\s\S]*?;").Value;
        if (string.IsNullOrEmpty(_appSettingsSeedSql))
            throw new InvalidOperationException("Could not find the app_settings seed row in init.sql.");

        Factory = new IntegrationApiFactory(ConnectionString);
        _ = Factory.Server; // boot the host once, up front
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
            await Factory.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        try
        {
            _server?.Dispose(); // pg_ctl stop + delete the instance directory
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Server is stopped; a leftover directory is removed by CleanupStaleInstances later.
        }
    }

    /// <summary>Empties every table and restores the single app_settings row from init.sql.</summary>
    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        if (_truncateSql is null)
        {
            var tables = new List<string>();
            await using (var cmd = new NpgsqlCommand(
                "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'", connection))
            await using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                    tables.Add('"' + reader.GetString(0) + '"');
            }
            _truncateSql = $"TRUNCATE TABLE {string.Join(", ", tables)} RESTART IDENTITY CASCADE;";
        }

        try
        {
            await using var reset = new NpgsqlCommand("SET lock_timeout = '10s';" + _truncateSql + _appSettingsSeedSql, connection);
            await reset.ExecuteNonQueryAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            throw new InvalidOperationException("Reset blocked by another session:\n" + await DescribeOtherSessionsAsync(), ex);
        }
    }

    private async Task<string> DescribeOtherSessionsAsync()
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT pid, datname, state, xact_start, left(query, 300) FROM pg_stat_activity WHERE pid <> pg_backend_pid() AND datname IS NOT NULL",
            connection);
        await using var reader = await cmd.ExecuteReaderAsync();
        var lines = new List<string>();
        while (await reader.ReadAsync())
            lines.Add(string.Join(" | ", Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i)))));
        return string.Join("\n", lines);
    }

    public AppDbContext CreateDbContext(string? connectionString = null) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString ?? ConnectionString).Options);

    /// <summary>Creates an empty database (no schema) and returns its connection string.</summary>
    public async Task<string> CreateEmptyDatabaseAsync(string prefix)
    {
        var name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 33, 63)];
        await ExecuteAdminAsync($"CREATE DATABASE \"{name}\"");
        return BuildConnectionString(name);
    }

    public async Task DropDatabaseAsync(string connectionString)
    {
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database!;
        NpgsqlConnection.ClearAllPools();
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }

    public async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        await cmd.ExecuteNonQueryAsync();
    }

    private string BuildConnectionString(string database) =>
        new NpgsqlConnectionStringBuilder
        {
            Host = "localhost",
            Port = Port,
            Username = "postgres",
            Password = "postgres",
            Database = database,
            Pooling = true
        }.ConnectionString;

    public static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task WaitUntilAcceptingConnectionsAsync(string connectionString)
    {
        // PgServer only waits for the TCP port to accept; Postgres can still answer
        // "the database system is starting up" for a moment after that.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString);
                await connection.OpenAsync();
                return;
            }
            catch (Exception) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
    }

    private static void CleanupStaleInstances()
    {
        // A crashed/killed run can leave an instance directory behind; binaries/ is the cache.
        var embedRoot = Path.Combine(CacheRoot, "pg_embed");
        if (!Directory.Exists(embedRoot))
            return;
        foreach (var dir in Directory.GetDirectories(embedRoot))
        {
            if (string.Equals(Path.GetFileName(dir), "binaries", StringComparison.OrdinalIgnoreCase))
                continue;
            if (Directory.GetLastWriteTimeUtc(dir) > DateTime.UtcNow.AddMinutes(-30))
                continue;
            try { Directory.Delete(dir, recursive: true); } catch { /* still in use — leave it */ }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DayGrid.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate DayGrid.sln above " + AppContext.BaseDirectory);
    }
}
