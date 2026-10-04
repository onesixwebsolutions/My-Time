using System.Reflection;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Email;
using DayGrid.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DayGrid.IntegrationTests;

/// <summary>
/// Runs exactly one tick of each hosted service against the real database, through the app's
/// own DI container (Npgsql DbContext, faked email sender). The tick methods are private, so
/// they are invoked directly rather than waiting on the 60-second timers.
/// </summary>
public class BackgroundServiceTests : IntegrationTestBase
{
    public BackgroundServiceTests(PostgresFixture fixture) : base(fixture) { }

    private IServiceScopeFactory ScopeFactory => Fx.Factory.Services.GetRequiredService<IServiceScopeFactory>();

    private static Task InvokeTickAsync(object service, string method) =>
        (Task)service.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(service, new object[] { CancellationToken.None })!;

    private async Task<(Reminder Due, Reminder ItemDue, Reminder Future)> SeedRemindersAsync()
    {
        await using var db = NewDb();
        var task = new FutureTask { Title = "Pay bill ✓", DueDate = new DateOnly(2026, 10, 1), DueTime = new TimeOnly(18, 0) };
        var checklist = new Checklist { Name = "C" };
        var item = new ChecklistItem { ChecklistId = checklist.Id, Title = "Vitamins", AnchorTime = new TimeOnly(9, 0) };
        var due = new Reminder { FutureTaskId = task.Id, OffsetMinutes = 30, FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5), Channels = NotificationChannel.InApp | NotificationChannel.Email };
        var itemDue = new Reminder { ChecklistItemId = item.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) };
        var future = new Reminder { FutureTaskId = task.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow.AddDays(1) };
        db.AddRange(task, checklist, item, due, itemDue, future);
        await db.SaveChangesAsync();
        return (due, itemDue, future);
    }

    [Fact]
    public async Task ReminderDispatcher_OneTick_SendsDueReminders()
    {
        var (due, itemDue, future) = await SeedRemindersAsync();
        var service = new ReminderDispatcherService(ScopeFactory, NullLogger<ReminderDispatcherService>.Instance);

        await InvokeTickAsync(service, "ProcessDueRemindersAsync");

        await using var db = NewDb();
        var reminders = await db.Reminders.AsNoTracking().ToDictionaryAsync(r => r.Id);
        Assert.Equal(ReminderStatus.Sent, reminders[due.Id].Status);
        Assert.Equal(1, reminders[due.Id].AttemptCount);
        Assert.NotNull(reminders[due.Id].SentAtUtc);
        Assert.Equal(ReminderStatus.Sent, reminders[itemDue.Id].Status);
        Assert.Equal(ReminderStatus.Scheduled, reminders[future.Id].Status);

        var logs = await db.NotificationLogs.AsNoTracking().OrderBy(n => n.Title).ToListAsync();
        Assert.Equal(new[] { "Pay bill ✓", "Vitamins" }, logs.Select(l => l.Title));
        Assert.Equal("Due 2026-10-01 at 18:00", logs[0].Body);
        Assert.Equal("Scheduled for 09:00", logs[1].Body);

        var email = Assert.Single(Fx.Factory.Email.Sent);
        Assert.Equal("onesixwebsolutions@gmail.com", email.To);

        // Second tick: nothing left to send.
        await InvokeTickAsync(service, "ProcessDueRemindersAsync");
        Assert.Single(Fx.Factory.Email.Sent);
    }

    [Fact]
    public async Task ReminderDispatcher_EmailFailure_MarksFailedAndLogsError()
    {
        var (due, _, _) = await SeedRemindersAsync();
        Fx.Factory.Email.FailWith = new EmailNotConfiguredException("not configured"); // no retry back-off
        var service = new ReminderDispatcherService(ScopeFactory, NullLogger<ReminderDispatcherService>.Instance);

        await InvokeTickAsync(service, "ProcessDueRemindersAsync");

        await using var db = NewDb();
        var reminder = await db.Reminders.AsNoTracking().SingleAsync(r => r.Id == due.Id);
        Assert.Equal(ReminderStatus.Failed, reminder.Status);
        var errorLog = await db.NotificationLogs.AsNoTracking().SingleAsync(n => n.Channel == NotificationChannel.Email);
        Assert.Equal("not configured", errorLog.Error);
        Assert.Equal(due.Id, errorLog.ReminderId);
    }

    [Fact]
    public async Task DailyDigest_OneTick_DoesNotThrow_WithAndWithoutDigestTime()
    {
        var clock = Fx.Factory.Services.GetRequiredService<IAppClock>();
        var service = new DailyDigestService(ScopeFactory, NullLogger<DailyDigestService>.Instance, clock);

        await using (var db = NewDb())
        {
            var settings = await db.AppSettings.SingleAsync();
            settings.DailyDigestTime = TimeOnly.FromDateTime(clock.Now.DateTime); // fires this minute
            await db.SaveChangesAsync();
        }
        await InvokeTickAsync(service, "CheckAndFireAsync");
        await InvokeTickAsync(service, "CheckAndFireAsync"); // same day: guarded

        await using (var db = NewDb())
        {
            var settings = await db.AppSettings.SingleAsync();
            settings.DailyDigestTime = null;
            await db.SaveChangesAsync();
        }
        await InvokeTickAsync(service, "CheckAndFireAsync");

        await using (var db = NewDb())
            await db.AppSettings.ExecuteDeleteAsync(); // no settings row at all
        await InvokeTickAsync(service, "CheckAndFireAsync");
    }
}
