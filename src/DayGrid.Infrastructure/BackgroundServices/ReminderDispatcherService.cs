using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Notifications;
using DayGrid.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DayGrid.Infrastructure.BackgroundServices;

/// <summary>
/// 60-second tick: finds due reminders and fans them out to their enabled channels.
///
/// There is no HttpContext here, so the tick works per user explicitly: it lists the users that
/// have due reminders, then processes each one in its own DI scope that acts as that user
/// (<see cref="CurrentUserContext.ActAs"/>), so tenant filters and stamping apply exactly as in a
/// request. Email goes to that user's confirmed account address (never anybody else's), and only
/// when the user's settings have email enabled. In-app notifications are pushed to that user's
/// SignalR connections only.
///
/// Production note: plan section 5.8 specifies `SELECT ... FOR UPDATE SKIP LOCKED` so a second
/// API instance can never double-send a reminder. This does the simpler, single-instance-safe
/// thing: read a batch and flip each row's status in one SaveChanges call. If this ever runs with
/// more than one replica, swap the due-reminder query for a `FromSqlRaw` with
/// `FOR UPDATE SKIP LOCKED` before anything else changes.
/// </summary>
public class ReminderDispatcherService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReminderDispatcherService> _logger;

    public ReminderDispatcherService(IServiceScopeFactory scopeFactory, ILogger<ReminderDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        do
        {
            try
            {
                await ProcessDueRemindersAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Reminder dispatcher tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ProcessDueRemindersAsync(CancellationToken ct)
    {
        var nowUtc = DateTimeOffset.UtcNow;

        List<Guid> userIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Legacy rows (user_id NULL) wait until the first account claims them.
            userIds = await db.Reminders.IgnoreQueryFilters().AsNoTracking()
                .Where(r => r.UserId != null && r.Status == ReminderStatus.Scheduled && r.FireAtUtc <= nowUtc)
                .Select(r => r.UserId!.Value)
                .Distinct()
                .ToListAsync(ct);
        }

        foreach (var userId in userIds)
        {
            try
            {
                await ProcessUserAsync(userId, nowUtc, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One user's failure must not starve everybody else's reminders.
                _logger.LogError(ex, "Reminder dispatch failed for user {UserId}", userId);
            }
        }
    }

    private async Task ProcessUserAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        scope.ServiceProvider.GetService<CurrentUserContext>()?.ActAs(userId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var notifier = scope.ServiceProvider.GetService<IUserNotifier>();

        // The explicit UserId predicates keep this correct even in a system (unfiltered) context.
        var due = await db.Reminders
            .Where(r => r.UserId == userId && r.Status == ReminderStatus.Scheduled && r.FireAtUtc <= nowUtc)
            .OrderBy(r => r.FireAtUtc)
            .Take(100)
            .ToListAsync(ct);
        if (due.Count == 0)
            return;

        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
        var account = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.EmailConfirmed })
            .FirstOrDefaultAsync(ct);
        var emailTo = account is { EmailConfirmed: true } ? account.Email : null;
        var emailEnabled = settings?.EmailEnabled ?? false;

        var inApp = new List<NotificationLog>();
        foreach (var reminder in due)
        {
            try
            {
                await DispatchAsync(db, emailSender, reminder, emailEnabled, emailTo, nowUtc, inApp, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // One bad reminder must not abort the batch — it is first in FireAtUtc order, so
                // an unhandled throw here would block every reminder behind it on every tick.
                reminder.Status = ReminderStatus.Failed;
                reminder.AttemptCount++;
                _logger.LogError(ex, "Reminder {ReminderId} could not be dispatched", reminder.Id);
            }
        }

        await db.SaveChangesAsync(ct);

        if (notifier is null)
            return;
        foreach (var log in inApp)
        {
            try
            {
                await notifier.ReminderFiredAsync(userId,
                    new { log.Id, log.ReminderId, log.Title, log.Body, log.Channel, log.CreatedAtUtc, log.ReadAtUtc }, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Real-time push of notification {NotificationId} failed", log.Id);
            }
        }
    }

    private async Task DispatchAsync(
        AppDbContext db, IEmailSender emailSender, Reminder reminder,
        bool emailEnabled, string? emailTo, DateTimeOffset nowUtc, List<NotificationLog> inApp, CancellationToken ct)
    {
        var (title, body) = await BuildPayloadAsync(db, reminder, ct);

        if (reminder.Channels.HasFlag(NotificationChannel.InApp))
        {
            var log = new NotificationLog
            {
                UserId = reminder.UserId,
                ReminderId = reminder.Id,
                Title = title,
                Body = body,
                Channel = NotificationChannel.InApp,
                CreatedAtUtc = nowUtc
            };
            db.NotificationLogs.Add(log);
            inApp.Add(log);
        }

        if (reminder.Channels.HasFlag(NotificationChannel.Email) && emailEnabled && !string.IsNullOrWhiteSpace(emailTo))
        {
            try
            {
                await SendWithRetryAsync(emailSender, emailTo!, title, body, ct);
            }
            catch (Exception ex)
            {
                reminder.Status = ReminderStatus.Failed;
                reminder.AttemptCount++;
                db.NotificationLogs.Add(new NotificationLog
                {
                    UserId = reminder.UserId,
                    ReminderId = reminder.Id,
                    Title = title,
                    Body = body,
                    Channel = NotificationChannel.Email,
                    CreatedAtUtc = nowUtc,
                    Error = ex.Message
                });
                _logger.LogWarning(ex, "Email reminder {ReminderId} failed after retries", reminder.Id);
                return; // in-app copy (if any) already landed above, so nothing is silently lost
            }
        }

        reminder.Status = ReminderStatus.Sent;
        reminder.SentAtUtc = nowUtc;
        reminder.AttemptCount++;
    }

    private static async Task<(string Title, string Body)> BuildPayloadAsync(AppDbContext db, Reminder reminder, CancellationToken ct)
    {
        if (reminder.FutureTaskId is { } futureTaskId)
        {
            var task = await db.FutureTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == futureTaskId && t.UserId == reminder.UserId, ct);
            var title = task?.Title ?? "Reminder";
            var body = task?.DueTime is { } dueTime
                ? $"Due {task.DueDate:yyyy-MM-dd} at {dueTime:HH:mm}"
                : $"Due {task?.DueDate:yyyy-MM-dd}";
            return (title, body);
        }

        if (reminder.ChecklistItemId is { } itemId)
        {
            var item = await db.ChecklistItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId && i.UserId == reminder.UserId, ct);
            var title = item?.Title ?? "Reminder";
            var body = item?.AnchorTime is { } anchorTime ? $"Scheduled for {anchorTime:HH:mm}" : "Scheduled today";
            return (title, body);
        }

        return ("Reminder", string.Empty);
    }

    private static async Task SendWithRetryAsync(IEmailSender sender, string to, string subject, string body, CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromSeconds(2);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await sender.SendAsync(to, subject, body, ct);
                return;
            }
            catch (Exception ex) when (attempt < maxAttempts && ex is not EmailNotConfiguredException && !ct.IsCancellationRequested)
            {
                await Task.Delay(delay, ct);
                delay *= 2; // exponential backoff
            }
        }
    }
}
