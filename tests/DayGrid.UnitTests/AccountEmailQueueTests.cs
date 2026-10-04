using DayGrid.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DayGrid.UnitTests;

/// <summary>The background account-email queue: flood protection, bounded size, failure isolation.</summary>
public class AccountEmailQueueTests
{
    private static readonly EmailContent Content = new("Subject", "<p>Body</p>");

    private static AccountEmailQueue NewQueue(FakeTimeProvider time, int cooldown = 60, int daily = 10, int capacity = 1000) =>
        new(Options.Create(new AccountEmailOptions { CooldownSeconds = cooldown, DailyLimitPerRecipient = daily, QueueCapacity = capacity }),
            time, NullLogger<AccountEmailQueue>.Instance);

    private static AccountEmail Mail(string to, AccountEmailKind kind) => new(to, kind, Content);

    [Fact]
    public void SameKindToSameRecipient_IsDroppedWithinTheCooldown_OtherKindsAndRecipientsAreNot()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var queue = NewQueue(time);

        Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.False(queue.Enqueue(Mail("A@Example.test", AccountEmailKind.ConfirmEmail)));      // case-insensitive recipient
        Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ResetPassword)));
        Assert.True(queue.Enqueue(Mail("b@example.test", AccountEmailKind.ConfirmEmail)));

        time.Advance(TimeSpan.FromSeconds(59));
        Assert.False(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.Equal(4, queue.Pending);
    }

    [Fact]
    public void DailyLimitPerRecipient_DropsExtras_UntilTheRollingDayHasPassed()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var queue = NewQueue(time, cooldown: 60, daily: 10);

        for (var i = 0; i < 10; i++)
        {
            Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)), $"email {i + 1}");
            time.Advance(TimeSpan.FromMinutes(1));
        }
        Assert.False(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.False(queue.Enqueue(Mail("a@example.test", AccountEmailKind.PasswordChanged)));
        Assert.True(queue.Enqueue(Mail("other@example.test", AccountEmailKind.ConfirmEmail)));

        time.Advance(TimeSpan.FromDays(1) - TimeSpan.FromMinutes(9)); // the first one is now a day old
        Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
    }

    [Fact]
    public void ZeroCooldownAndLimit_DisableThrottling()
    {
        var queue = NewQueue(new FakeTimeProvider(DateTimeOffset.UtcNow), cooldown: 0, daily: 0);
        for (var i = 0; i < 50; i++)
            Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
    }

    [Fact]
    public void FullQueue_DropsInsteadOfBlocking()
    {
        var queue = NewQueue(new FakeTimeProvider(DateTimeOffset.UtcNow), cooldown: 0, daily: 0, capacity: 2);
        Assert.True(queue.Enqueue(Mail("a@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.True(queue.Enqueue(Mail("b@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.False(queue.Enqueue(Mail("c@example.test", AccountEmailKind.ConfirmEmail)));
        Assert.Equal(2, queue.Pending);
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<string> Sent { get; } = new();
        public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
        {
            if (toAddress.StartsWith("fail", StringComparison.Ordinal))
                throw new InvalidOperationException("SMTP down");
            lock (Sent) Sent.Add(toAddress);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Dispatcher_SendsInTheBackground_AndAFailureDoesNotStopTheQueue()
    {
        var sender = new RecordingSender();
        var services = new ServiceCollection().AddSingleton<IEmailSender>(sender).BuildServiceProvider();
        var queue = NewQueue(new FakeTimeProvider(DateTimeOffset.UtcNow), cooldown: 0, daily: 0);
        using var dispatcher = new AccountEmailDispatcher(queue, services.GetRequiredService<IServiceScopeFactory>(), NullLogger<AccountEmailDispatcher>.Instance);
        await dispatcher.StartAsync(CancellationToken.None);
        try
        {
            queue.Enqueue(Mail("fail@example.test", AccountEmailKind.ResetPassword));
            queue.Enqueue(Mail("ok@example.test", AccountEmailKind.ResetPassword));

            Assert.True(await queue.WaitForIdleAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(new[] { "ok@example.test" }, sender.Sent);
            Assert.Equal(0, queue.Pending);
        }
        finally
        {
            await dispatcher.StopAsync(CancellationToken.None);
        }
    }
}
