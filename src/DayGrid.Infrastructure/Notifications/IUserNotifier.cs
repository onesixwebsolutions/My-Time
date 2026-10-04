namespace DayGrid.Infrastructure.Notifications;

/// <summary>
/// Pushes real-time events to one user's connected clients (SignalR, implemented in DayGrid.Api).
/// Never broadcasts: every event is addressed to exactly one user id.
/// </summary>
public interface IUserNotifier
{
    Task ReminderFiredAsync(Guid userId, object notification, CancellationToken ct = default);
}
