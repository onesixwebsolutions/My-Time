using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DayGrid.Api.Hubs;

/// <summary>
/// SignalR hub at <c>/hubs/schedule</c>. The server side is intentionally near-empty — clients
/// only need something to connect to; every event is pushed from elsewhere (mainly
/// <c>ReminderDispatcherService</c> and the CRUD endpoints) via
/// <see cref="IHubContext{ScheduleHub}"/>, not via methods invoked on this class.
///
/// Events broadcast on this hub (plan section 5.7):
///
/// <list type="bullet">
/// <item><description><c>NowBlockChanged</c> — payload: current + next block. Fires when a timetable
/// block boundary is crossed.</description></item>
/// <item><description><c>ReminderFired</c> — payload: notification DTO. Fires when the dispatcher
/// sends a reminder.</description></item>
/// <item><description><c>ItemCompleted</c> — payload: <c>{ itemId, date, status }</c>. Keeps other
/// open tabs in sync when a checklist item is ticked.</description></item>
/// <item><description><c>PlanInvalidated</c> — payload: <c>{ date }</c>. Fires when a
/// template/assignment is edited so clients know to refetch <c>/today</c>.</description></item>
/// </list>
///
/// Requires an authenticated, email-confirmed user (cookie auth; anonymous negotiate gets 401).
/// Events are only ever sent to <c>Clients.User(userId)</c> — never broadcast — via
/// <see cref="SignalRUserNotifier"/>.
/// </summary>
[Authorize]
public class ScheduleHub : Hub
{
}
