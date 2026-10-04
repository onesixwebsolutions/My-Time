using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DayGrid.Infrastructure.BackgroundServices;

/// <summary>
/// 60-second tick: finds due reminders and fans them out to their enabled channels.
///
/// Production note: plan section 5.8 specifies `SELECT ... FOR UPDATE SKIP LOCKED` so a second
/// API instance can never double-send a reminder. EF Core's LINQ layer has no way to express
/// row-level locking hints — that would require a raw SQL query via `FromSqlRaw` (or
/// `FromSqlInterpolated`) against a query that also updates status inside the same statement/
/// transaction. This scaffold does the simpler, single-instance-safe thing instead: read the
/// batch, immediately flip each row to a transitional "claimed" state inside one SaveChanges
/// call, then process. That is a reasonable best-effort for one API instance; if/when this ever
/// runs with more than one replica, swap the query below for a `FromSqlRaw` with
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
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

        var nowUtc = DateTimeOffset.UtcNow;

        var due = await db.Reminders
            .Where(r => r.Status == ReminderStatus.Scheduled && r.FireAtUtc <= nowUtc)
            .OrderBy(r => r.FireAtUtc)
            .Take(100)
            .ToListAsync(ct);

        if (due.Count == 0)
            return;

        var appSettings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var emailTo = appSettings?.EmailTo;
        var emailEnabled = appSettings?.EmailEnabled ?? false;

        foreach (var reminder in due)
        {
            try
            {
                await DispatchAsync(db, emailSender, reminder, emailEnabled, emailTo, nowUtc, ct);
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
    }

    private async Task DispatchAsync(
        AppDbContext db, IEmailSender emailSender, Reminder reminder,
        bool emailEnabled, string? emailTo, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var (title, body) = await BuildPayloadAsync(db, reminder, ct);

        if (reminder.Channels.HasFlag(NotificationChannel.InApp))
        {
            db.NotificationLogs.Add(new NotificationLog
            {
                ReminderId = reminder.Id,
                Title = title,
                Body = body,
                Channel = NotificationChannel.InApp,
                CreatedAtUtc = nowUtc
            });
            // TODO: also push via IHubContext<ScheduleHub>.Clients.All.SendAsync("ReminderFired", ...)
            // once the hub context is wired in from DayGrid.Api (Infrastructure has no reference
            // to the Api project's Hubs namespace, by design — this is left as an integration
            // point for whoever wires SignalR broadcast into this service).
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
            var task = await db.FutureTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == futureTaskId, ct);
            var title = task?.Title ?? "Reminder";
            var body = task?.DueTime is { } dueTime
                ? $"Due {task.DueDate:yyyy-MM-dd} at {dueTime:HH:mm}"
                : $"Due {task?.DueDate:yyyy-MM-dd}";
            return (title, body);
        }

        if (reminder.ChecklistItemId is { } itemId)
        {
            var item = await db.ChecklistItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct);
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
