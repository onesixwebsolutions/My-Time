using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DayGrid.UnitTests;

public class MailKitEmailSenderTests
{
    private static MailKitEmailSender Sender(EmailSettings settings) =>
        new(Options.Create(settings), NullLogger<MailKitEmailSender>.Instance);

    private static EmailSettings Settings(string host = "smtp.invalid", string user = "someone@example.com", string password = "real-password") =>
        new() { Host = host, Port = 587, User = user, Password = password };

    [Theory]
    [InlineData("smtp.invalid", "someone@example.com", "CHANGE_ME")]
    [InlineData("smtp.invalid", "someone@example.com", "change_me")]
    [InlineData("smtp.invalid", "someone@example.com", "<app-password>")]
    [InlineData("smtp.invalid", "someone@example.com", "")]
    [InlineData("smtp.invalid", "someone@example.com", "   ")]
    [InlineData("", "someone@example.com", "real-password")]
    [InlineData("smtp.invalid", "", "real-password")]
    public async Task PlaceholderOrMissingSettings_ThrowEmailNotConfigured_WithoutTouchingNetwork(string host, string user, string password)
    {
        // smtp.invalid would fail DNS if a connection were attempted; the guard must throw first.
        await Assert.ThrowsAsync<EmailNotConfiguredException>(
            () => Sender(Settings(host, user, password)).SendAsync("to@example.com", "subject", "<p>body</p>"));
    }

    [Fact]
    public void EmailNotConfiguredException_IsAnInvalidOperationException()
    {
        Assert.IsAssignableFrom<InvalidOperationException>(new EmailNotConfiguredException("x"));
    }
}

public class ReminderDispatcherServiceTests
{
    private sealed class RecordingEmailSender : IEmailSender
    {
        private readonly Exception? _throw;
        public int Calls;

        public RecordingEmailSender(Exception? toThrow = null) => _throw = toThrow;

        public string? LastTo;

        public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
        {
            LastTo = toAddress;
            Interlocked.Increment(ref Calls);
            return _throw is null ? Task.CompletedTask : Task.FromException(_throw);
        }
    }

    // Fire times far in the past so the reminders are due regardless of the wall clock.
    private static readonly DateTimeOffset LongAgo = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static (ServiceProvider Provider, string DbName) BuildServices(IEmailSender sender)
    {
        var dbName = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton(sender);
        return (services.BuildServiceProvider(), dbName);
    }

    private static async Task RunOneTickAsync(ServiceProvider provider, Func<AppDbContext, Task<bool>> done)
    {
        var service = new ReminderDispatcherService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ReminderDispatcherService>.Instance);

        await service.StartAsync(CancellationToken.None);
        try
        {
            // The first tick runs immediately on start; wait (bounded) for it to persist.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (true)
            {
                using var scope = provider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (await done(db)) break;
                if (DateTime.UtcNow > deadline) throw new TimeoutException("Dispatcher tick did not complete.");
                await Task.Delay(25);
            }
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<Reminder> SeedAsync(ServiceProvider provider, NotificationChannel channels, bool emailEnabled, bool emailConfirmed = true)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new AppUser { Id = Guid.NewGuid(), UserName = "me@example.com", Email = "me@example.com", EmailConfirmed = emailConfirmed, DisplayName = "Me" };
        db.Users.Add(user);
        db.AppSettings.Add(new AppSetting { UserId = user.Id, EmailEnabled = emailEnabled, EmailTo = "someone-else@example.com" });
        var task = new FutureTask { UserId = user.Id, Title = "Pay rent", DueDate = new DateOnly(2000, 1, 1), DueTime = new TimeOnly(9, 0) };
        var reminder = new Reminder { UserId = user.Id, FutureTaskId = task.Id, FireAtUtc = LongAgo, Channels = channels };
        db.FutureTasks.Add(task);
        db.Reminders.Add(reminder);
        await db.SaveChangesAsync();
        return reminder;
    }

    private static Task<bool> NotScheduled(AppDbContext db, Guid id) =>
        db.Reminders.AsNoTracking().AnyAsync(r => r.Id == id && r.Status != ReminderStatus.Scheduled);

    [Fact]
    public async Task InAppReminder_IsMarkedSent_AndLogsNotification()
    {
        var sender = new RecordingEmailSender();
        var (provider, _) = BuildServices(sender);
        await using var _ = provider;
        var reminder = await SeedAsync(provider, NotificationChannel.InApp, emailEnabled: true);

        await RunOneTickAsync(provider, db => NotScheduled(db, reminder.Id));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatus.Sent, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.NotNull(saved.SentAtUtc);
        var log = await db.NotificationLogs.SingleAsync();
        Assert.Equal(saved.UserId, log.UserId); // stamped with the reminder's owner
        Assert.Equal("Pay rent", log.Title);
        Assert.Equal("Due 2000-01-01 at 09:00", log.Body);
        Assert.Equal(NotificationChannel.InApp, log.Channel);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task EmailNotConfigured_FailsReminderOnce_WithoutRetrying()
    {
        var sender = new RecordingEmailSender(new EmailNotConfiguredException("not configured"));
        var (provider, _) = BuildServices(sender);
        await using var _ = provider;
        var reminder = await SeedAsync(provider, NotificationChannel.InApp | NotificationChannel.Email, emailEnabled: true);

        await RunOneTickAsync(provider, db => NotScheduled(db, reminder.Id));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.Reminders.SingleAsync();
        Assert.Equal(ReminderStatus.Failed, saved.Status);
        Assert.Equal(1, saved.AttemptCount);
        Assert.Equal(1, sender.Calls); // no retry/backoff for a send that can never succeed
        Assert.Equal("me@example.com", sender.LastTo); // the account address, not settings.EmailTo

        var logs = await db.NotificationLogs.ToListAsync();
        Assert.Contains(logs, l => l.Channel == NotificationChannel.InApp && l.Error == null);
        Assert.Contains(logs, l => l.Channel == NotificationChannel.Email && l.Error == "not configured");
    }

    [Fact]
    public async Task EmailChannel_WhenEmailDisabledInSettings_SkipsSend_AndMarksSent()
    {
        var sender = new RecordingEmailSender();
        var (provider, _) = BuildServices(sender);
        await using var _ = provider;
        var reminder = await SeedAsync(provider, NotificationChannel.Email, emailEnabled: false);

        await RunOneTickAsync(provider, db => NotScheduled(db, reminder.Id));

        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(ReminderStatus.Sent, (await db.Reminders.SingleAsync()).Status);
        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task EmailChannel_WhenAccountEmailUnconfirmed_SkipsSend()
    {
        var sender = new RecordingEmailSender();
        var (provider, _) = BuildServices(sender);
        await using var _ = provider;
        var reminder = await SeedAsync(provider, NotificationChannel.Email, emailEnabled: true, emailConfirmed: false);

        await RunOneTickAsync(provider, db => NotScheduled(db, reminder.Id));

        Assert.Equal(0, sender.Calls);
    }

    [Fact]
    public async Task LegacyReminderWithoutOwner_IsLeftScheduled()
    {
        var sender = new RecordingEmailSender();
        var (provider, _) = BuildServices(sender);
        await using var _ = provider;
        Guid legacyId, ownedId;
        using (var scope = provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var legacyTask = new FutureTask { Title = "Legacy", DueDate = new DateOnly(2000, 1, 1) };
            var legacy = new Reminder { FutureTaskId = legacyTask.Id, FireAtUtc = LongAgo };
            db.AddRange(legacyTask, legacy);
            await db.SaveChangesAsync();
            legacyId = legacy.Id;
        }
        ownedId = (await SeedAsync(provider, NotificationChannel.InApp, emailEnabled: false)).Id;

        await RunOneTickAsync(provider, db => NotScheduled(db, ownedId));

        using var check = provider.CreateScope();
        var reminders = check.ServiceProvider.GetRequiredService<AppDbContext>().Reminders.AsNoTracking();
        Assert.Equal(ReminderStatus.Scheduled, (await reminders.SingleAsync(r => r.Id == legacyId)).Status);
        Assert.Equal(ReminderStatus.Sent, (await reminders.SingleAsync(r => r.Id == ownedId)).Status);
    }
}
