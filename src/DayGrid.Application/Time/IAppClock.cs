namespace DayGrid.Application.Time;

/// <summary>
/// The user's wall clock. The server may run in any zone (UTC on Azure App Service), but
/// "today" and "now" in this app always mean the user's local date/time in that user's time zone
/// (app_settings.time_zone, falling back to <c>App:TimeZone</c>). In a request, the injected
/// IAppClock is the signed-in user's clock; background jobs build one per user via IAppClockFactory.
/// Use this instead of <see cref="DateTime.Now"/>/<see cref="DateTime.Today"/> for anything user-facing; keep persisted audit timestamps (CreatedAt etc.) in UTC.
/// </summary>
public interface IAppClock
{
    /// <summary>The user's time zone.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>The current instant, expressed in <see cref="TimeZone"/> (offset included).</summary>
    DateTimeOffset Now { get; }

    /// <summary>The user's current local date.</summary>
    DateOnly Today { get; }

    /// <summary>The user's current local time of day.</summary>
    TimeOnly TimeOfDay { get; }
}
