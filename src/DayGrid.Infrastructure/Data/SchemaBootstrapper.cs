using Microsoft.Extensions.Logging;
using Npgsql;

namespace DayGrid.Infrastructure.Data;

/// <summary>
/// Applies the hand-written db/init.sql schema (there are no EF migrations). Shared by the
/// embedded-Postgres first run (EmbeddedDatabase) and the External-mode startup bootstrap
/// (Database:InitializeSchema = true, e.g. a fresh Azure Database for PostgreSQL).
///
/// init.sql is not idempotent, so <see cref="EnsureSchemaAsync"/> guards it with an existence
/// check on a known table and applies the whole script in one transaction — a failure leaves the
/// database untouched rather than half-created.
/// </summary>
public static class SchemaBootstrapper
{
    /// <summary>A table created by init.sql; its presence means the schema is already there.</summary>
    public const string MarkerTable = "checklists";

    public static async Task<bool> SchemaExistsAsync(NpgsqlConnection connection, CancellationToken ct = default)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @table)",
            connection);
        command.Parameters.AddWithValue("table", MarkerTable);
        return (bool)(await command.ExecuteScalarAsync(ct))!;
    }

    /// <summary>Executes init.sql unconditionally, inside a single transaction.</summary>
    public static async Task ApplySchemaAsync(NpgsqlConnection connection, string schemaSqlPath, CancellationToken ct = default)
    {
        var schemaSql = await File.ReadAllTextAsync(schemaSqlPath, ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await using (var command = new NpgsqlCommand(schemaSql, connection, transaction))
        {
            await command.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <summary>Executes init.sql unconditionally against <paramref name="connectionString"/>.</summary>
    public static async Task ApplySchemaAsync(string connectionString, string schemaSqlPath, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await ApplySchemaAsync(connection, schemaSqlPath, ct);
    }

    /// <summary>
    /// Creates the schema from init.sql if <see cref="MarkerTable"/> does not exist yet.
    /// Returns true if the schema was created, false if it was already present.
    /// </summary>
    public static async Task<bool> EnsureSchemaAsync(string connectionString, string schemaSqlPath, ILogger logger, CancellationToken ct = default)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);

        if (await SchemaExistsAsync(connection, ct))
        {
            logger.LogInformation("Database schema already present (table '{Table}' exists) — skipping init.sql", MarkerTable);
            return false;
        }

        if (!File.Exists(schemaSqlPath))
            throw new FileNotFoundException("Schema script not found — cannot initialize the database.", schemaSqlPath);

        logger.LogWarning("Database schema not found (no '{Table}' table) — applying {SchemaSqlPath}", MarkerTable, schemaSqlPath);
        await ApplySchemaAsync(connection, schemaSqlPath, ct);
        logger.LogInformation("Database schema created from init.sql");
        return true;
    }
}
