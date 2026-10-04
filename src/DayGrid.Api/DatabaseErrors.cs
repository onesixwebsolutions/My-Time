using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DayGrid.Api;

/// <summary>
/// Maps PostgreSQL errors caused by the request's data to 4xx responses. Endpoints validate the
/// common cases themselves; this is the safety net for the rest (e.g. a 121-character checklist
/// name, an amount beyond numeric(12,2)), which would otherwise surface as a 500.
/// </summary>
public static class DatabaseErrors
{
    public static int? ToStatusCode(Exception? error) => Find(error)?.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => StatusCodes.Status409Conflict,
        PostgresErrorCodes.StringDataRightTruncation           // value too long for varchar(n)
            or PostgresErrorCodes.NumericValueOutOfRange       // beyond numeric(12,2) / smallint
            or PostgresErrorCodes.CharacterNotInRepertoire     // NUL (\u0000) in text
            or PostgresErrorCodes.UntranslatableCharacter
            or PostgresErrorCodes.DatetimeFieldOverflow
            or PostgresErrorCodes.ForeignKeyViolation
            or PostgresErrorCodes.CheckViolation
            or PostgresErrorCodes.NotNullViolation => StatusCodes.Status400BadRequest,
        _ => null
    };

    public static string Describe(Exception error) => Find(error)?.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => "The request conflicts with an existing record.",
        PostgresErrorCodes.StringDataRightTruncation => "A text value is longer than its maximum length.",
        PostgresErrorCodes.NumericValueOutOfRange => "A numeric value is out of range.",
        PostgresErrorCodes.CharacterNotInRepertoire or PostgresErrorCodes.UntranslatableCharacter => "A text value contains an unsupported character.",
        PostgresErrorCodes.ForeignKeyViolation => "The request references a record that does not exist.",
        _ => "The request was invalid."
    };

    private static PostgresException? Find(Exception? error)
    {
        for (var e = error; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg)
                return pg;
            if (e is not DbUpdateException && e is not InvalidOperationException)
                break;
        }
        return null;
    }
}
