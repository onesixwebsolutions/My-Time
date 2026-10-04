using System.Text;
using Npgsql;

namespace DayGrid.IntegrationTests.Infrastructure;

/// <summary>
/// The embedded Postgres binaries ship without psql, and db/seed.sql relies on a handful of psql
/// features. This runs a script with exactly that subset: <c>\set</c> / <c>\echo</c> (ignored —
/// every statement stops on error anyway), <c>RETURNING ... \gset</c> and <c>:'var'</c>
/// substitution. Statements end at a top-level <c>;</c> or <c>\gset</c>.
/// </summary>
public static class PsqlScriptRunner
{
    public static async Task<int> RunAsync(NpgsqlConnection connection, string script)
    {
        var variables = new Dictionary<string, string>();
        var buffer = new StringBuilder();
        var executed = 0;
        var inQuote = false;

        foreach (var rawLine in script.Replace("\r\n", "\n").Split('\n'))
        {
            if (!inQuote && string.IsNullOrWhiteSpace(buffer.ToString()) && rawLine.TrimStart().StartsWith('\\'))
                continue; // \set ON_ERROR_STOP on, \echo ...

            var line = new StringBuilder();
            var terminator = (string?)null;
            for (var i = 0; i < rawLine.Length; i++)
            {
                var c = rawLine[i];
                if (inQuote)
                {
                    line.Append(c);
                    if (c == '\'') inQuote = false; // '' re-enters on the next char
                    continue;
                }
                if (c == '\'') { inQuote = true; line.Append(c); continue; }
                if (c == '-' && i + 1 < rawLine.Length && rawLine[i + 1] == '-') break; // comment
                if (c == ':' && i + 1 < rawLine.Length && rawLine[i + 1] == '\'')
                {
                    var end = rawLine.IndexOf('\'', i + 2);
                    var name = rawLine[(i + 2)..end];
                    if (!variables.TryGetValue(name, out var value))
                        throw new InvalidOperationException($"psql variable '{name}' is not set");
                    line.Append('\'').Append(value.Replace("'", "''")).Append('\'');
                    i = end;
                    continue;
                }
                if (c == '\\' && rawLine.AsSpan(i).StartsWith("\\gset")) { terminator = "gset"; break; }
                if (c == ';') { terminator = ";"; line.Append(c); break; }
                line.Append(c);
            }

            buffer.Append(line).Append('\n');
            if (terminator is null)
                continue;

            var sql = buffer.ToString();
            buffer.Clear();
            await using var command = new NpgsqlCommand(sql, connection);
            if (terminator == "gset")
            {
                await using var reader = await command.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("\\gset query returned no rows: " + sql);
                for (var f = 0; f < reader.FieldCount; f++)
                    variables[reader.GetName(f)] = Convert.ToString(reader.GetValue(f))!;
            }
            else
            {
                await command.ExecuteNonQueryAsync();
            }
            executed++;
        }

        if (buffer.ToString().Trim().Length > 0)
            throw new InvalidOperationException("Unterminated statement at end of script: " + buffer);
        return executed;
    }
}
