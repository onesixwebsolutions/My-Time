using DayGrid.Application.Time;
using TimeZoneConverter;

namespace DayGrid.Infrastructure.Time;

/// <summary>
/// <see cref="IAppClock"/> backed by a <see cref="TimeProvider"/> (TimeProvider.System in
/// production, a fake in tests) and an IANA (or Windows) time zone id.
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
    // InvariantGlobalization=true (as DayGrid.Api does). TimeZoneConverter carries its own
    // IANA <-> Windows mapping, so IANA ids (and Windows ids) resolve on every platform.
    internal static TimeZoneInfo ResolveTimeZone(string id)
    {
        if (TryResolveTimeZone(id, out var zone))
            return zone;

        throw new TimeZoneNotFoundException(
            $"Time zone '{id}' could not be resolved on this machine. Use an IANA id such as 'Asia/Kolkata'.");
    }

    public static bool TryResolveTimeZone(string? id, out TimeZoneInfo zone)
    {
        zone = TimeZoneInfo.Utc;
        if (string.IsNullOrWhiteSpace(id))
            return false;
        try
        {
            if (TZConvert.TryGetTimeZoneInfo(id.Trim(), out var found))
            {
                zone = found;
                return true;
            }
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // fall through
        }
        return false;
    }

    /// <summary>True for a known IANA time zone id (what the API accepts from clients).</summary>
    public static bool IsKnownIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id)
        && TZConvert.KnownIanaTimeZoneNames.Contains(id.Trim(), StringComparer.Ordinal)
        && TryResolveTimeZone(id, out _);

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset Now => TimeZoneInfo.ConvertTime(_timeProvider.GetUtcNow(), TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);

    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
}
