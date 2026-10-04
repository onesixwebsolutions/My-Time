using DayGrid.Application.Time;

namespace DayGrid.Infrastructure.Time;

/// <summary>
/// <see cref="IAppClock"/> backed by a <see cref="TimeProvider"/> (TimeProvider.System in
/// production, a fake in tests) and an IANA/Windows time zone id. .NET 8 resolves IANA ids such
/// as "Asia/Kolkata" on both Windows and Linux.
/// </summary>
public sealed class AppClock : IAppClock
{
    public const string DefaultTimeZoneId = "Asia/Kolkata";

    private readonly TimeProvider _timeProvider;

    public AppClock(TimeProvider timeProvider, string? timeZoneId)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        TimeZone = ResolveTimeZone(string.IsNullOrWhiteSpace(timeZoneId) ? DefaultTimeZoneId : timeZoneId.Trim());
    }

    // Windows resolves IANA ids only through ICU, which is unavailable when the app runs with
    // InvariantGlobalization=true (as DayGrid.Api does). Linux reads tzdata directly, so the first
    // lookup succeeds there; on Windows fall back to the Windows id for the zones this app uses.
    private static readonly Dictionary<string, string> IanaToWindowsFallback = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asia/Kolkata"] = "India Standard Time",
        ["Asia/Calcutta"] = "India Standard Time",
        ["Etc/UTC"] = "UTC",
        ["UTC"] = "UTC"
    };

    private static TimeZoneInfo ResolveTimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException)
        {
            if ((TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var converted)
                 || TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out converted)
                 || IanaToWindowsFallback.TryGetValue(id, out converted))
                && TimeZoneInfo.TryFindSystemTimeZoneById(converted!, out var zone))
            {
                return zone;
            }

            throw new TimeZoneNotFoundException(
                $"App:TimeZone '{id}' could not be resolved on this machine. Use an IANA id (e.g. 'Asia/Kolkata') on Linux or a Windows id (e.g. 'India Standard Time') on Windows.");
        }
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
}
