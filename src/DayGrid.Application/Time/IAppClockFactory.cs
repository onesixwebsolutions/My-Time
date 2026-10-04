namespace DayGrid.Application.Time;

/// <summary>
/// Creates <see cref="IAppClock"/> instances for a given time zone. Time zones are per user
/// (app_settings.time_zone); <see cref="DefaultTimeZoneId"/> (config <c>App:TimeZone</c>) is the
/// fallback for new accounts and for a stored id that can no longer be resolved.
/// </summary>
public interface IAppClockFactory
{
    string DefaultTimeZoneId { get; }

    /// <summary>A clock in <paramref name="timeZoneId"/>, or in the default zone if it is null/unknown.</summary>
    IAppClock ForTimeZone(string? timeZoneId);

    /// <summary>True when <paramref name="timeZoneId"/> is a known IANA time zone id.</summary>
    bool IsValidTimeZone(string? timeZoneId);
}
