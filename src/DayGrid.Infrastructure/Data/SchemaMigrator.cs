using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace DayGrid.Infrastructure.Data;

/// <summary>A versioned SQL migration (db/migrations/NNNN_name.sql).</summary>
public sealed record SchemaMigration(string Version, string Name, string Sql);

/// <summary>
/// Applies the versioned, hand-written SQL migrations in db/migrations (embedded into this
/// assembly) in order. There are no EF migrations; the EF model is kept in step with these
/// scripts by the schema-parity integration tests.
///
/// <list type="bullet">
/// <item>Applied versions are tracked in <c>schema_migrations(version text PK, applied_at timestamptz)</c>.</item>
/// <item>Each migration runs in its own transaction together with its tracking row, so a failing
/// script leaves the database exactly as it was before that script.</item>
/// <item>A database created from the old db/init.sql (tables present, no schema_migrations) is
/// baselined at 0001 — after verifying that every 0001 table really exists.</item>
/// <item>A session-level advisory lock serialises concurrent starts (e.g. several App Service
/// instances booting at once) so a migration is never applied twice.</item>
/// </list>
/// </summary>
public static class SchemaMigrator
{
    /// <summary>Arbitrary constant key for pg_advisory_lock ("DayGrid" migrations).</summary>
    private const long AdvisoryLockKey = 0x4461794772696401;

    public const string BaselineVersion = "0001";

    /// <summary>Tables created by 0001_initial.sql — all must exist to baseline a legacy database.</summary>
    public static readonly IReadOnlyList<string> InitialTables =
    [
        "checklists", "checklist_items", "checklist_completions", "timetable_templates", "timetable_blocks",
        "timetable_assignments", "day_overrides", "future_tasks", "reminders", "notification_log",
        "app_settings", "simple_tasks", "constant_expenses", "varying_expenses", "completed_spends"
    ];

    private static readonly Regex ResourceName = new(@"^DayGrid\.Migrations\.(?<version>\d{4})_(?<name>[A-Za-z0-9_]+)\.sql$");

    /// <summary>All migrations shipped with this build, ordered by version.</summary>
    public static IReadOnlyList<SchemaMigration> LoadEmbedded()
    {
        var assembly = typeof(SchemaMigrator).Assembly;
        var migrations = new List<SchemaMigration>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            var match = ResourceName.Match(resource);
            if (!match.Success)
                continue;
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            migrations.Add(new SchemaMigration(match.Groups["version"].Value, match.Groups["name"].Value, reader.ReadToEnd()));
        }

        var ordered = migrations.OrderBy(m => m.Version, StringComparer.Ordinal).ToList();
        if (ordered.Count == 0 || ordered[0].Version != BaselineVersion)
            throw new InvalidOperationException("Embedded schema migrations are missing (expected db/migrations/0001_initial.sql).");
        if (ordered.Select(m => m.Version).Distinct().Count() != ordered.Count)
            throw new InvalidOperationException("Duplicate schema migration versions: " + string.Join(", ", ordered.Select(m => m.Version)));
        return ordered;
    }

    /// <summary>Brings the database at <paramref name="connectionString"/> up to the latest migration.
    /// Returns the versions applied by this call (empty when already up to date).</summary>
    public static Task<IReadOnlyList<string>> MigrateAsync(string connectionString, ILogger logger, CancellationToken ct = default) =>
        MigrateAsync(connectionString, LoadEmbedded(), logger, ct);

    public static async Task<IReadOnlyList<string>> MigrateAsync(
        string connectionString, IReadOnlyList<SchemaMigration> migrations, ILogger logger, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await ExecuteAsync(connection, null, $"SELECT pg_advisory_lock({AdvisoryLockKey})", ct);
        try
        {
            return await MigrateLockedAsync(connection, migrations, logger, ct);
        }
        finally
        {
            await ExecuteAsync(connection, null, $"SELECT pg_advisory_unlock({AdvisoryLockKey})", CancellationToken.None);
        }
    }

    private static async Task<IReadOnlyList<string>> MigrateLockedAsync(
        NpgsqlConnection connection, IReadOnlyList<SchemaMigration> migrations, ILogger logger, CancellationToken ct)
    {
        var trackingExisted = await TableExistsAsync(connection, "schema_migrations", ct);
        if (!trackingExisted)
        {
            await ExecuteAsync(connection, null,
                "CREATE TABLE schema_migrations (version text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())", ct);
        }

        var applied = await AppliedVersionsAsync(connection, ct);

        if (applied.Count == 0 && await TableExistsAsync(connection, "checklists", ct))
        {
            // A database built from the old db/init.sql: it has the 0001 schema but no history.
            var missing = new List<string>();
            foreach (var table in InitialTables)
                if (!await TableExistsAsync(connection, table, ct))
                    missing.Add(table);
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    "The database has a partial DayGrid schema (missing: " + string.Join(", ", missing) +
                    "). It cannot be baselined automatically — restore it or create the missing tables first.");

            await ExecuteAsync(connection, null, $"INSERT INTO schema_migrations (version) VALUES ('{BaselineVersion}')", ct);
            applied.Add(BaselineVersion);
            logger.LogWarning("Existing DayGrid schema without migration history detected — baselined at {Version}", BaselineVersion);
        }

        var unknown = applied.Where(v => migrations.All(m => m.Version != v)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException(
                "The database has schema migrations this build does not know (" + string.Join(", ", unknown) +
                ") — it was migrated by a newer version of DayGrid. Refusing to start against it.");

        var newlyApplied = new List<string>();
        foreach (var migration in migrations.Where(m => !applied.Contains(m.Version)))
        {
            logger.LogWarning("Applying schema migration {Version}_{Name}", migration.Version, migration.Name);
            await using var transaction = await connection.BeginTransactionAsync(ct);
            await ExecuteAsync(connection, transaction, migration.Sql, ct);
            await using (var track = new NpgsqlCommand("INSERT INTO schema_migrations (version) VALUES (@v)", connection, transaction))
            {
                track.Parameters.AddWithValue("v", migration.Version);
                await track.ExecuteNonQueryAsync(ct);
            }
            await transaction.CommitAsync(ct);
            newlyApplied.Add(migration.Version);
        }

        if (newlyApplied.Count == 0)
            logger.LogInformation("Database schema is up to date (latest migration {Version})", migrations[^1].Version);
        else
            logger.LogInformation("Applied schema migrations: {Versions}", string.Join(", ", newlyApplied));

        return newlyApplied;
    }

    /// <summary>Versions recorded in schema_migrations (empty if the table does not exist).</summary>
    public static async Task<IReadOnlyList<string>> GetAppliedVersionsAsync(string connectionString, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        if (!await TableExistsAsync(connection, "schema_migrations", ct))
            return [];
        return (await AppliedVersionsAsync(connection, ct)).OrderBy(v => v, StringComparer.Ordinal).ToList();
    }

    private static async Task<HashSet<string>> AppliedVersionsAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var versions = new HashSet<string>(StringComparer.Ordinal);
        await using var cmd = new NpgsqlCommand("SELECT version FROM schema_migrations", connection);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            versions.Add(reader.GetString(0));
        return versions;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @table)",
            connection);
        command.Parameters.AddWithValue("table", table);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction) { CommandTimeout = 300 };
        await command.ExecuteNonQueryAsync(ct);
    }
}
