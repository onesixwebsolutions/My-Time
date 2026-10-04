using DayGrid.Application.Security;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Infrastructure.Time;

/// <summary>
/// Scoped <see cref="IAppClock"/> for the current user: the time zone stored in their
/// app_settings row (read once per scope, lazily), falling back to <c>App:TimeZone</c>.
/// </summary>
public sealed class UserAppClock : IAppClock
{
    private readonly Lazy<IAppClock> _clock;

    public UserAppClock(IAppClockFactory factory, ICurrentUser currentUser, AppDbContext db)
    {
        _clock = new Lazy<IAppClock>(() =>
        {
            var userId = currentUser.UserId;
            if (userId is null)
                return factory.ForTimeZone(null);
            var timeZoneId = db.AppSettings.IgnoreQueryFilters().AsNoTracking()
                .Where(s => s.UserId == userId)
                .Select(s => s.TimeZone)
                .FirstOrDefault();
            return factory.ForTimeZone(timeZoneId);
        });
    }

    public TimeZoneInfo TimeZone => _clock.Value.TimeZone;
    public DateTimeOffset Now => _clock.Value.Now;
    public DateOnly Today => _clock.Value.Today;
    public TimeOnly TimeOfDay => _clock.Value.TimeOfDay;
}
