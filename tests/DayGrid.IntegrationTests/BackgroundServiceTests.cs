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

        // Sent to the owner's confirmed account address.
        var email = Assert.Single(Fx.Factory.Email.Sent);
        Assert.Equal(UserEmail, email.To);
        Assert.All(logs, l => Assert.Equal(UserId, l.UserId));

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
    public async Task DailyDigest_FiresPerUser_InThatUsersOwnTimeZone_OncePerDay()
    {
        var factory = Fx.Factory.Services.GetRequiredService<IAppClockFactory>();
        var service = new DailyDigestService(ScopeFactory, NullLogger<DailyDigestService>.Instance, factory);

        var (other, otherId, _) = await SignInAnotherUserAsync();
        other.Dispose();
        const string otherZone = "America/New_York";
        var istNow = factory.ForTimeZone("Asia/Kolkata").Now;
        var nyNow = factory.ForTimeZone(otherZone).Now;
        Assert.NotEqual(istNow.Hour, nyNow.Hour);

        await using (var db = NewSystemDb())
        {
            var mine = await db.AppSettings.SingleAsync(s => s.UserId == UserId);
            mine.DailyDigestTime = TimeOnly.FromDateTime(istNow.DateTime); // fires this minute in IST
            var theirs = await db.AppSettings.SingleAsync(s => s.UserId == otherId);
            theirs.TimeZone = otherZone;
            theirs.DailyDigestTime = TimeOnly.FromDateTime(istNow.DateTime); // same wall time, but not "now" in New York
            await db.SaveChangesAsync();
        }

        await InvokeTickAsync(service, "CheckAndFireAsync");
        Assert.Equal(new[] { UserId }, service.LastTickFired);

        await InvokeTickAsync(service, "CheckAndFireAsync"); // same day: guarded
        Assert.Empty(service.LastTickFired);

        await using (var db = NewSystemDb())
        {
            var theirs = await db.AppSettings.SingleAsync(s => s.UserId == otherId);
            theirs.DailyDigestTime = TimeOnly.FromDateTime(nyNow.DateTime); // now it is their minute
            await db.SaveChangesAsync();
        }
        await InvokeTickAsync(service, "CheckAndFireAsync");
        Assert.Equal(new[] { otherId }, service.LastTickFired);

        await using (var db = NewSystemDb())
            await db.AppSettings.ExecuteDeleteAsync(); // no settings rows at all
        await InvokeTickAsync(service, "CheckAndFireAsync");
        Assert.Empty(service.LastTickFired);
    }

    [Fact]
    public async Task ReminderDispatcher_SendsEachUsersRemindersToThatUserOnly()
    {
        await SeedRemindersAsync(); // test user's
        var (other, otherId, otherEmail) = await SignInAnotherUserAsync();
        other.Dispose();
        await using (var db = Fx.CreateUserDbContext(otherId))
        {
            var task = new FutureTask { Title = "Their task", DueDate = new DateOnly(2026, 10, 1) };
            db.AddRange(task, new Reminder { FutureTaskId = task.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2), Channels = NotificationChannel.InApp | NotificationChannel.Email });
            await db.SaveChangesAsync();
        }

        var service = new ReminderDispatcherService(ScopeFactory, NullLogger<ReminderDispatcherService>.Instance);
        await InvokeTickAsync(service, "ProcessDueRemindersAsync");

        Assert.Equal(new[] { otherEmail, UserEmail }.OrderBy(e => e), Fx.Factory.Email.Sent.Select(m => m.To).OrderBy(e => e));
        Assert.Contains(Fx.Factory.Email.Sent, m => m.To == otherEmail && m.Subject == "Their task");
        Assert.DoesNotContain(Fx.Factory.Email.Sent, m => m.To == UserEmail && m.Subject == "Their task");

        await using var sys = NewSystemDb();
        var theirLogs = await sys.NotificationLogs.Where(n => n.UserId == otherId).ToListAsync();
        Assert.Equal(new[] { "Their task" }, theirLogs.Select(l => l.Title));
        Assert.DoesNotContain(await sys.NotificationLogs.Where(n => n.UserId == UserId).Select(n => n.Title).ToListAsync(), t => t == "Their task");
    }
}
