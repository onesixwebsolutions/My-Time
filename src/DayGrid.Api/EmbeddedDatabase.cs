using DayGrid.Infrastructure.Data;
using MysticMind.PostgresEmbed;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace DayGrid.Api;

/// <summary>
/// Starts a private, self-contained Postgres instance for machines that don't have Postgres
/// installed — the point of Database:Mode = "Embedded" (the default in appsettings.json; local
/// dev overrides it back to "External" in appsettings.Development.json to keep using your own
/// Postgres/docker-compose unchanged). The instance lives entirely under
/// %LocalAppData%\DayGrid\pgdata, is not a system service, and is started/stopped alongside the
/// app — nothing else on the machine is touched.
///
/// First run creates the "daygrid" database. Every run (first or not) then applies any pending
/// schema migrations (db/migrations, see SchemaMigrator) — so an existing desktop install is
/// upgraded in place — and data persists across restarts exactly like a normal installed
/// database would.
///
/// Known limitation: PgServer downloads the actual Postgres binaries from the network the first
/// time it runs on a machine (cached afterward). A machine with no internet access on first
/// launch will fail here — there's no fully offline path yet.
/// </summary>
public static class EmbeddedDatabase
{
    private const string PgVersion = "16.2.0";
    private const string SuperuserName = "postgres";
    private const string SuperuserPassword = "postgres";
    private const string DatabaseName = "daygrid";

    // PgServer generates a new random data subdirectory under dbDir on every run unless given a
    // fixed instanceId — without this, "reusing the existing pgdata folder" silently reused only
    // the *parent* folder while each run got a brand-new, empty Postgres cluster underneath it
    // (caught by testing: a restart came back with "database daygrid does not exist" even though
    // the previous run's data was sitting right there in a sibling directory). One fixed GUID
    // means every run resolves to the exact same data directory: dbDir/pg_embed/{InstanceId}/data.
    private static readonly Guid InstanceId = Guid.Parse("6d3f2f8a-6c1a-4a2b-9e3a-1a2b3c4d5e6f");

    /// <summary>
    /// postgres.exe's stderr is inherited from pg_ctl, which PgServer starts with redirected
    /// stdout/stderr pipes that it never reads. Postgres logs every ERROR and checkpoint there, so
    /// once that pipe's buffer filled the next backend to log blocked forever while holding its
    /// locks, freezing the whole database (reproduced by the integration tests after a few hundred
    /// constraint violations; a long-running desktop session would hit it via checkpoint logs).
    /// The logging collector moves server logging to files under the data directory's log/ folder.
    /// </summary>
    public static Dictionary<string, string> ServerParameters() => new()
    {
        ["logging_collector"] = "on"
    };

    public static async Task<(PgServer Server, string ConnectionString)> StartAsync(string dataDir)
    {
        var actualDataDir = Path.Combine(dataDir, "pg_embed", InstanceId.ToString(), "data");
        var isFirstRun = !Directory.Exists(actualDataDir);

        Console.WriteLine(isFirstRun
            ? $"[EmbeddedDatabase] First run — initializing a private Postgres instance in {actualDataDir}"
            : $"[EmbeddedDatabase] Starting existing Postgres instance in {actualDataDir}");

        var server = new PgServer(
            PgVersion,
            dbDir: dataDir,
            instanceId: InstanceId,
            pgServerParams: ServerParameters(),
            clearInstanceDirOnStop: false,
            clearWorkingDirOnStart: false);

        await server.StartAsync();

        var adminConnectionString = BuildConnectionString(server.PgPort, "postgres");
        var appConnectionString = BuildConnectionString(server.PgPort, DatabaseName);

        await WaitUntilAcceptingConnectionsAsync(adminConnectionString);

        // Also covers a first run that died between initdb and CREATE DATABASE.
        await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
        {
            await adminConnection.OpenAsync();
            await using var existsCommand = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @name)", adminConnection);
            existsCommand.Parameters.AddWithValue("name", DatabaseName);
            if (!(bool)(await existsCommand.ExecuteScalarAsync())!)
            {
                await using var createDbCommand = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", adminConnection);
                await createDbCommand.ExecuteNonQueryAsync();
            }
        }

        var applied = await SchemaMigrator.MigrateAsync(appConnectionString, NullLogger.Instance);
        if (applied.Count > 0)
            Console.WriteLine($"[EmbeddedDatabase] Applied schema migrations {string.Join(", ", applied)} — the daygrid database is ready.");

        return (server, appConnectionString);
    }

    // PgServer.StartAsync can return while Postgres is still in crash recovery (e.g. after an
    // unclean stop), when connections are rejected with 57P03 "the database system is starting up".
    private static async Task WaitUntilAcceptingConnectionsAsync(string connectionString)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                await using var connection = new NpgsqlConnection(connectionString + ";Pooling=false");
                await connection.OpenAsync();
                return;
            }
            catch (Exception ex) when (DateTime.UtcNow < deadline &&
                ex is PostgresException { SqlState: "57P03" } or NpgsqlException { IsTransient: true })
            {
                await Task.Delay(250);
            }
        }
    }

    private static string BuildConnectionString(int port, string database) =>
        $"Host=localhost;Port={port};Username={SuperuserName};Password={SuperuserPassword};Database={database}";
}
