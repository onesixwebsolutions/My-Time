using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

public record CreateReminderRequest(int OffsetMinutes, List<string> Channels);
public record CreateFutureTaskRequest(
    string Title, string? Notes, DateOnly DueDate, TimeOnly? DueTime,
    string? Category, Priority Priority, Guid? PromoteToChecklistId, List<CreateReminderRequest>? Reminders);
public record UpdateFutureTaskRequest(
    string Title, string? Notes, DateOnly DueDate, TimeOnly? DueTime,
    string? Category, Priority Priority, Guid? PromoteToChecklistId, List<CreateReminderRequest>? Reminders);
public record UpdateFutureTaskStatusRequest(FutureTaskStatus Status);
public record DeferFutureTaskRequest(DateOnly NewDueDate);

public static class FutureTasksEndpoints
{
    public static IEndpointRouteBuilder MapFutureTaskEndpoints(this IEndpointRouteBuilder app)
    {
        MapFutureTasks(app);
        MapReminders(app);
        MapNotifications(app);
        return app;
    }

    private static void MapFutureTasks(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/future-tasks").WithTags("Future Tasks");

        group.MapGet("/", async (AppDbContext db, FutureTaskStatus? status, DateOnly? from, DateOnly? to, string? q, CancellationToken ct) =>
        {
            var query = db.FutureTasks.AsNoTracking().Include(t => t.Reminders).AsQueryable();
            if (status is { } s) query = query.Where(t => t.Status == s);
            if (from is { } f) query = query.Where(t => t.DueDate >= f);
            if (to is { } toDate) query = query.Where(t => t.DueDate <= toDate);
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(t => EF.Functions.ILike(t.Title, SearchPattern.Contains(q), SearchPattern.Escape));

            var tasks = await query.OrderBy(t => t.DueDate).ThenBy(t => t.DueTime).ToListAsync(ct);
            return Results.Ok(tasks.Select(ToDto));
        })
        .WithName("ListFutureTasks");

        group.MapGet("/upcoming", async (AppDbContext db, IAppClock clock, int days, CancellationToken ct) =>
        {
            var horizon = days <= 0 ? 30 : Math.Min(days, 3660);
            var today = clock.Today;
            var end = today.AddDays(horizon);

            var tasks = await db.FutureTasks.AsNoTracking()
                .Where(t => t.Status == FutureTaskStatus.Pending && t.DueDate >= today && t.DueDate <= end)
                .OrderBy(t => t.DueDate).ThenBy(t => t.DueTime)
                .ToListAsync(ct);

            var tomorrow = today.AddDays(1);
            var weekEnd = today.AddDays(7);

            var grouped = new
            {
                today = tasks.Where(t => t.DueDate == today).Select(ToDto),
                tomorrow = tasks.Where(t => t.DueDate == tomorrow).Select(ToDto),
                thisWeek = tasks.Where(t => t.DueDate > tomorrow && t.DueDate <= weekEnd).Select(ToDto),
                later = tasks.Where(t => t.DueDate > weekEnd).Select(ToDto)
            };

            return Results.Ok(grouped);
        })
        .WithName("ListUpcomingFutureTasks");

        group.MapPost("/", async (AppDbContext db, IAppClock clock, CreateFutureTaskRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return TitleRequired();
            if (await ChecklistMissingAsync(db, request.PromoteToChecklistId, ct))
                return ChecklistNotFound();
            if (HasInvalidOffset(request.Reminders))
                return InvalidOffset();

            var task = new FutureTask
            {
                Title = request.Title.Trim(),
                Notes = request.Notes,
                DueDate = request.DueDate,
                DueTime = request.DueTime,
                Category = request.Category,
                Priority = request.Priority,
                PromoteToChecklistId = request.PromoteToChecklistId
            };

            db.FutureTasks.Add(task);

            foreach (var reminderRequest in request.Reminders ?? new List<CreateReminderRequest>())
                db.Reminders.Add(BuildReminder(task, reminderRequest, clock.TimeZone));

            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/future-tasks/{task.Id}", ToDto(task));
        })
        .WithName("CreateFutureTask");

        group.MapPut("/{id:guid}", async (AppDbContext db, IAppClock clock, Guid id, UpdateFutureTaskRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return TitleRequired();

            var task = await db.FutureTasks.FindAsync([id], ct);
            if (task is null) return Results.NotFound();
            if (await ChecklistMissingAsync(db, request.PromoteToChecklistId, ct))
                return ChecklistNotFound();
            if (HasInvalidOffset(request.Reminders))
                return InvalidOffset();

            task.Title = request.Title.Trim();
            task.Notes = request.Notes;
            task.DueDate = request.DueDate;
            task.DueTime = request.DueTime;
            task.Category = request.Category;
            task.Priority = request.Priority;
            task.PromoteToChecklistId = request.PromoteToChecklistId;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            if (request.Reminders is not null)
            {
                // Due date/time moved (or reminders were explicitly resent) — recompute the whole
                // set inside this same save, so a deferred task never fires on its old schedule
                // (plan section 5.8).
                var existing = await db.Reminders.Where(r => r.FutureTaskId == id).ToListAsync(ct);
                db.Reminders.RemoveRange(existing);
                foreach (var reminderRequest in request.Reminders)
                    db.Reminders.Add(BuildReminder(task, reminderRequest, clock.TimeZone));
            }
            else
            {
                // Reminders not resent: still move the pending ones with the new due date/time,
                // otherwise they keep firing on the old schedule.
                var scheduled = await db.Reminders
                    .Where(r => r.FutureTaskId == id && r.Status == ReminderStatus.Scheduled)
                    .ToListAsync(ct);
                foreach (var reminder in scheduled)
                    reminder.FireAtUtc = ComputeFireAtUtc(task.DueDate, task.DueTime, reminder.OffsetMinutes, clock.TimeZone);
            }

            await db.SaveChangesAsync(ct);
            await db.Entry(task).Collection(t => t.Reminders).LoadAsync(ct);
            return Results.Ok(ToDto(task));
        })
        .WithName("UpdateFutureTask");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var task = await db.FutureTasks.FindAsync([id], ct);
            if (task is null) return Results.NotFound();

            db.FutureTasks.Remove(task); // cascades to reminders
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteFutureTask");

        group.MapPatch("/{id:guid}/status", async (AppDbContext db, Guid id, UpdateFutureTaskStatusRequest request, CancellationToken ct) =>
        {
            var task = await db.FutureTasks.FindAsync([id], ct);
            if (task is null) return Results.NotFound();

            task.Status = request.Status;
            task.CompletedAt = request.Status == FutureTaskStatus.Done ? DateTimeOffset.UtcNow : null;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            if (request.Status is FutureTaskStatus.Done or FutureTaskStatus.Cancelled)
            {
                var pendingReminders = await db.Reminders
                    .Where(r => r.FutureTaskId == id && r.Status == ReminderStatus.Scheduled)
                    .ToListAsync(ct);
                foreach (var reminder in pendingReminders)
                    reminder.Status = ReminderStatus.Cancelled;
            }

            await db.SaveChangesAsync(ct);
            await db.Entry(task).Collection(t => t.Reminders).LoadAsync(ct);
            return Results.Ok(ToDto(task));
        })
        .WithName("SetFutureTaskStatus");

        group.MapPatch("/{id:guid}/defer", async (AppDbContext db, IAppClock clock, Guid id, DeferFutureTaskRequest request, CancellationToken ct) =>
        {
            // A body without newDueDate binds to default(DateOnly) and silently deferred to 0001-01-01.
            if (request.NewDueDate == default)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newDueDate"] = ["New due date is required."] });

            var task = await db.FutureTasks.FindAsync([id], ct);
            if (task is null) return Results.NotFound();

            task.DueDate = request.NewDueDate;
            task.Status = FutureTaskStatus.Deferred;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            var reminders = await db.Reminders.Where(r => r.FutureTaskId == id && r.Status == ReminderStatus.Scheduled).ToListAsync(ct);
            foreach (var reminder in reminders)
                reminder.FireAtUtc = ComputeFireAtUtc(task.DueDate, task.DueTime, reminder.OffsetMinutes, clock.TimeZone);

            await db.SaveChangesAsync(ct);
            await db.Entry(task).Collection(t => t.Reminders).LoadAsync(ct);
            return Results.Ok(ToDto(task));
        })
        .WithName("DeferFutureTask");
    }

    private static void MapReminders(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/future-tasks/{id:guid}/reminders").WithTags("Future Tasks");

        group.MapGet("/", async (AppDbContext db, Guid id, CancellationToken ct) =>
            Results.Ok((await db.Reminders.AsNoTracking().Where(r => r.FutureTaskId == id).ToListAsync(ct)).Select(ToReminderDto)))
            .WithName("ListFutureTaskReminders");

        group.MapPost("/", async (AppDbContext db, IAppClock clock, Guid id, CreateReminderRequest request, CancellationToken ct) =>
        {
            var task = await db.FutureTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (task is null) return Results.NotFound();
            if (HasInvalidOffset([request]))
                return InvalidOffset();

            var reminder = BuildReminder(task, request, clock.TimeZone);
            db.Reminders.Add(reminder);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/future-tasks/{id}/reminders/{reminder.Id}", ToReminderDto(reminder));
        })
        .WithName("CreateFutureTaskReminder");

        group.MapDelete("/{rid:guid}", async (AppDbContext db, Guid id, Guid rid, CancellationToken ct) =>
        {
            var reminder = await db.Reminders.FirstOrDefaultAsync(r => r.Id == rid && r.FutureTaskId == id, ct);
            if (reminder is null) return Results.NotFound();

            db.Reminders.Remove(reminder);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteFutureTaskReminder");
    }

    private static void MapNotifications(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications");

        group.MapGet("/", async (AppDbContext db, bool unreadOnly = false, int? take = null, CancellationToken ct = default) =>
        {
            var query = db.NotificationLogs.AsNoTracking().AsQueryable();
            if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);

            var results = await query
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(Math.Clamp(take ?? 50, 1, 500))
                .ToListAsync(ct);

            return Results.Ok(results);
        })
        .WithName("ListNotifications");

        group.MapPatch("/{id:guid}/read", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var notification = await db.NotificationLogs.FindAsync([id], ct);
            if (notification is null) return Results.NotFound();

            notification.ReadAtUtc = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(notification);
        })
        .WithName("MarkNotificationRead");

        group.MapPatch("/read-all", async (AppDbContext db, CancellationToken ct) =>
        {
            var unread = await db.NotificationLogs.Where(n => n.ReadAtUtc == null).ToListAsync(ct);
            var now = DateTimeOffset.UtcNow;
            foreach (var n in unread)
                n.ReadAtUtc = now;

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("MarkAllNotificationsRead");

        group.MapPost("/test", async (AppDbContext db, IEmailSender emailSender, ILogger<Program> logger, CancellationToken ct) =>
        {
            var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            db.NotificationLogs.Add(new NotificationLog
            {
                Title = "Test notification",
                Body = "If you can see this, in-app notifications are working.",
                Channel = NotificationChannel.InApp
            });

            string? emailError = null;
            if (settings?.EmailEnabled == true && !string.IsNullOrWhiteSpace(settings.EmailTo))
            {
                try
                {
                    await emailSender.SendAsync(settings.EmailTo, "DayGrid test notification", "<p>SMTP is configured correctly.</p>", ct);
                }
                catch (Exception ex)
                {
                    emailError = ex.Message;
                    logger.LogWarning(ex, "Test email failed");
                }
            }

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { inAppSent = true, emailAttempted = settings?.EmailEnabled == true, emailError });
        })
        .WithName("SendTestNotification");
    }

    // Projections rather than raw entities — FutureTask <-> Reminder is a bidirectional
    // navigation (Reminder.FutureTask), so serializing the tracked entity graph directly
    // (e.g. after .Include(t => t.Reminders)) throws a JSON cycle exception.
    private static object ToDto(FutureTask t) => new
    {
        t.Id,
        t.Title,
        t.Notes,
        t.DueDate,
        t.DueTime,
        t.Category,
        t.Priority,
        t.Status,
        t.CompletedAt,
        t.PromoteToChecklistId,
        t.CreatedAt,
        t.UpdatedAt,
        Reminders = t.Reminders.Select(ToReminderDto)
    };

    private static object ToReminderDto(Reminder r) => new
    {
        r.Id,
        r.FutureTaskId,
        r.OffsetMinutes,
        r.FireAtUtc,
        r.Channels,
        r.Status,
        r.SentAtUtc,
        r.AttemptCount
    };

    private static IResult TitleRequired() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

    // promote_to_checklist_id is a real FK — an unknown id used to fail the INSERT/UPDATE (500).
    private static async Task<bool> ChecklistMissingAsync(AppDbContext db, Guid? checklistId, CancellationToken ct) =>
        checklistId is { } id && !await db.Checklists.AnyAsync(c => c.Id == id, ct);

    private static IResult ChecklistNotFound() =>
        Results.ValidationProblem(new Dictionary<string, string[]> { ["promoteToChecklistId"] = ["Checklist not found."] });

    private const int MaxReminderOffsetMinutes = 366 * 24 * 60;

    private static bool HasInvalidOffset(IEnumerable<CreateReminderRequest>? reminders) =>
        reminders is not null && reminders.Any(r => r is null || r.OffsetMinutes is < 0 or > MaxReminderOffsetMinutes);

    private static IResult InvalidOffset() =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["offsetMinutes"] = [$"Reminder offset must be between 0 and {MaxReminderOffsetMinutes} minutes."]
        });

    private static Reminder BuildReminder(FutureTask task, CreateReminderRequest request, TimeZoneInfo timeZone)
    {
        var channels = NotificationChannel.None;
        foreach (var c in request.Channels ?? new List<string>())
        {
            if (Enum.TryParse<NotificationChannel>(c, ignoreCase: true, out var parsed))
                channels |= parsed;
        }

        return new Reminder
        {
            FutureTaskId = task.Id,
            OffsetMinutes = request.OffsetMinutes,
            Channels = channels == NotificationChannel.None ? NotificationChannel.InApp : channels,
            FireAtUtc = ComputeFireAtUtc(task.DueDate, task.DueTime, request.OffsetMinutes, timeZone)
        };
    }

    // Due date/time are wall-clock values in the user's zone (IAppClock.TimeZone, from
    // App:TimeZone) — not the server's zone, which is UTC on Azure App Service.
    private static DateTimeOffset ComputeFireAtUtc(DateOnly dueDate, TimeOnly? dueTime, int offsetMinutes, TimeZoneInfo timeZone)
    {
        var wallClock = dueDate.ToDateTime(dueTime ?? new TimeOnly(9, 0), DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(wallClock))
            wallClock = wallClock.AddHours(1); // skipped by a DST spring-forward
        var utc = TimeZoneInfo.ConvertTimeToUtc(wallClock, timeZone);
        return new DateTimeOffset(utc, TimeSpan.Zero).AddMinutes(-offsetMinutes);
    }
}
