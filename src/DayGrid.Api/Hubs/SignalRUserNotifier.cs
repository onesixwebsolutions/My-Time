using DayGrid.Infrastructure.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace DayGrid.Api.Hubs;

/// <summary>Pushes hub events to a single user's connections (user id = NameIdentifier claim).</summary>
public sealed class SignalRUserNotifier : IUserNotifier
{
    private readonly IHubContext<ScheduleHub> _hub;

    public SignalRUserNotifier(IHubContext<ScheduleHub> hub) => _hub = hub;

    public Task ReminderFiredAsync(Guid userId, object notification, CancellationToken ct = default) =>
        _hub.Clients.User(userId.ToString()).SendAsync("ReminderFired", notification, ct);
}
