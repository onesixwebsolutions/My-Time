using DayGrid.Infrastructure.Data;
using MysticMind.PostgresEmbed;
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
/// First run creates the "daygrid" database and applies db/init.sql. Every run after that just
/// starts Postgres against the same data directory, so data persists across restarts exactly
/// like a normal installed database would.
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

    public static async Task<(PgServer Server, string ConnectionString)> StartAsync(string dataDir, string schemaSqlPath)
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
            clearInstanceDirOnStop: false,
            clearWorkingDirOnStart: false);

        await server.StartAsync();

        var adminConnectionString = BuildConnectionString(server.PgPort, "postgres");
        var appConnectionString = BuildConnectionString(server.PgPort, DatabaseName);

        if (isFirstRun)
        {
            await using (var adminConnection = new NpgsqlConnection(adminConnectionString))
            {
                await adminConnection.OpenAsync();
                await using var createDbCommand = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", adminConnection);
                await createDbCommand.ExecuteNonQueryAsync();
            }

            await SchemaBootstrapper.ApplySchemaAsync(appConnectionString, schemaSqlPath);

            Console.WriteLine("[EmbeddedDatabase] Schema applied — the daygrid database is ready.");
        }

        return (server, appConnectionString);
    }

    private static string BuildConnectionString(int port, string database) =>
        $"Host=localhost;Port={port};Username={SuperuserName};Password={SuperuserPassword};Database={database}";
}
