using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DayGrid.Infrastructure.Email;

/// <summary>The kinds of account email (each has its own per-recipient cooldown).</summary>
public enum AccountEmailKind
{
    ConfirmEmail,
    ResetPassword,
    AlreadyRegistered,
    PasswordChanged
}

/// <summary>An account email waiting to be sent.</summary>
public sealed record AccountEmail(string To, AccountEmailKind Kind, EmailContent Content);

/// <summary>Account email throttling and queue size (config section <c>Email:AccountEmails</c>).</summary>
public sealed class AccountEmailOptions
{
    /// <summary>Messages that may wait in the queue; further messages are dropped (and logged).</summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>Minimum seconds between two emails of the same kind to one recipient (0 = no cooldown).</summary>
    public int CooldownSeconds { get; set; } = 60;

    /// <summary>Maximum account emails per recipient in any rolling 24 hours (0 = unlimited).</summary>
    public int DailyLimitPerRecipient { get; set; } = 10;
}

/// <summary>
/// Hands account emails (confirm, reset, already-registered, password-changed) to a background
/// sender so the auth endpoints answer in the same time whether or not an email goes out — and a
/// slow or failing mail server can never change an API response (account enumeration).
/// </summary>
public interface IAccountEmailQueue
{
    /// <summary>Queues <paramref name="email"/>. Returns false when it was dropped (recipient
    /// cooldown/daily limit, or a full queue). Never throws and never blocks.</summary>
    bool Enqueue(AccountEmail email);
}

/// <summary>
/// Bounded in-memory queue (<see cref="Channel{T}"/>) with per-recipient flood protection: at most
/// one email of each kind per recipient per cooldown, and a rolling daily cap per recipient. Extras
/// are dropped silently (logged), so the caller's response never reveals anything.
/// </summary>
public sealed class AccountEmailQueue : IAccountEmailQueue
{
    private const int PruneThreshold = 10_000;

    private readonly Channel<AccountEmail> _channel;
    private readonly AccountEmailOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<AccountEmailQueue> _logger;
    private readonly Dictionary<string, RecipientHistory> _history = new(StringComparer.OrdinalIgnoreCase);
    private int _pending;

    public AccountEmailQueue(IOptions<AccountEmailOptions> options, TimeProvider time, ILogger<AccountEmailQueue> logger)
    {
        _options = options.Value;
        _time = time;
        _logger = logger;
        _channel = Channel.CreateBounded<AccountEmail>(new BoundedChannelOptions(Math.Max(1, _options.QueueCapacity))
        {
            FullMode = BoundedChannelFullMode.Wait, // TryWrite then fails instead of evicting queued mail
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelReader<AccountEmail> Reader => _channel.Reader;

    /// <summary>Emails queued or being sent right now.</summary>
    public int Pending => Volatile.Read(ref _pending);

    public bool Enqueue(AccountEmail email)
    {
        if (!TryReserve(email.To, email.Kind))
        {
            _logger.LogWarning("Account email {Kind} to {To} dropped: recipient cooldown or daily limit reached", email.Kind, email.To);
            return false;
        }

        Interlocked.Increment(ref _pending);
        if (_channel.Writer.TryWrite(email))
            return true;

        Interlocked.Decrement(ref _pending);
        _logger.LogError("Account email {Kind} to {To} dropped: the send queue is full ({Capacity})", email.Kind, email.To, _options.QueueCapacity);
        return false;
    }

    /// <summary>Called by the sender once a message has been handled (sent or failed).</summary>
    internal void Completed() => Interlocked.Decrement(ref _pending);

    /// <summary>Waits until every queued email has been handled (tests, graceful checks).</summary>
    public async Task<bool> WaitForIdleAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (Pending > 0)
        {
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(10, ct);
        }
        return true;
    }

    private bool TryReserve(string to, AccountEmailKind kind)
    {
        var now = _time.GetUtcNow();
        var cooldown = TimeSpan.FromSeconds(Math.Max(0, _options.CooldownSeconds));
        var dailyLimit = Math.Max(0, _options.DailyLimitPerRecipient);
        var key = to.Trim();

        lock (_history)
        {
            if (_history.Count > PruneThreshold)
                Prune(now);

            if (!_history.TryGetValue(key, out var history))
                _history[key] = history = new RecipientHistory();

            while (history.Sent.Count > 0 && now - history.Sent.Peek() >= TimeSpan.FromDays(1))
                history.Sent.Dequeue();

            if (cooldown > TimeSpan.Zero && history.LastByKind.TryGetValue(kind, out var last) && now - last < cooldown)
                return false;
            if (dailyLimit > 0 && history.Sent.Count >= dailyLimit)
                return false;

            history.LastByKind[kind] = now;
            history.Sent.Enqueue(now);
            return true;
        }
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, history) in _history.ToList())
        {
            var latest = history.LastByKind.Count == 0 ? DateTimeOffset.MinValue : history.LastByKind.Values.Max();
            if (now - latest >= TimeSpan.FromDays(1))
                _history.Remove(key);
        }
    }

    private sealed class RecipientHistory
    {
        public Dictionary<AccountEmailKind, DateTimeOffset> LastByKind { get; } = new();
        public Queue<DateTimeOffset> Sent { get; } = new();
    }
}

/// <summary>Background sender for <see cref="AccountEmailQueue"/>. A failed send is logged and
/// dropped (account emails are best-effort; the user can ask again).</summary>
public sealed class AccountEmailDispatcher : BackgroundService
{
    private readonly AccountEmailQueue _queue;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<AccountEmailDispatcher> _logger;

    public AccountEmailDispatcher(AccountEmailQueue queue, IServiceScopeFactory scopes, ILogger<AccountEmailDispatcher> logger)
    {
        _queue = queue;
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var email in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await SendAsync(email, stoppingToken);
                }
                finally
                {
                    _queue.Completed();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Sends one message; never throws except for shutdown.</summary>
    public async Task SendAsync(AccountEmail email, CancellationToken ct)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
            await sender.SendAsync(email.To, email.Content.Subject, email.Content.HtmlBody, ct);
            _logger.LogInformation("Account email {Kind} sent to {To}", email.Kind, email.To);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex, "Could not send account email {Kind} to {To}", email.Kind, email.To);
        }
    }
}
