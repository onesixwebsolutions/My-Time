using DayGrid.Application.Time;

namespace DayGrid.Infrastructure.Time;

/// <summary>
/// Singleton <see cref="IAppClockFactory"/>: all clocks share one <see cref="TimeProvider"/>
/// (TimeProvider.System, a fake in tests). The default zone comes from config <c>App:TimeZone</c>
/// and is validated at construction, so a bad value fails at startup.
/// </summary>
public sealed class AppClockFactory : IAppClockFactory
{
    private readonly TimeProvider _timeProvider;
    private readonly AppClock _defaultClock;

    public AppClockFactory(TimeProvider timeProvider, string? defaultTimeZoneId)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        DefaultTimeZoneId = string.IsNullOrWhiteSpace(defaultTimeZoneId) ? AppClock.DefaultTimeZoneId : defaultTimeZoneId.Trim();
        _defaultClock = new AppClock(timeProvider, DefaultTimeZoneId);
    }

    public string DefaultTimeZoneId { get; }

    public IAppClock ForTimeZone(string? timeZoneId) =>
        !string.IsNullOrWhiteSpace(timeZoneId)
        && !string.Equals(timeZoneId.Trim(), DefaultTimeZoneId, StringComparison.Ordinal)
        && AppClock.TryResolveTimeZone(timeZoneId, out _)
            ? new AppClock(_timeProvider, timeZoneId)
            : _defaultClock;

    public bool IsValidTimeZone(string? timeZoneId) => AppClock.IsKnownIanaTimeZone(timeZoneId);
}
