using System.Collections.Concurrent;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DayGrid.Infrastructure.BackgroundServices;

/// <summary>
/// Once a minute, checks every user's <c>app_settings.daily_digest_time</c> against that user's
/// own wall clock (their time zone, not the server's) and triggers the "here's your day" digest
/// on the matching minute — at most once per user per local date.
/// </summary>
public class DailyDigestService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyDigestService> _logger;
    private readonly IAppClockFactory _clockFactory;
    private readonly ConcurrentDictionary<Guid, DateOnly> _lastFiredDate = new();

    public DailyDigestService(IServiceScopeFactory scopeFactory, ILogger<DailyDigestService> logger, IAppClockFactory clockFactory)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clockFactory = clockFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TickInterval);

        do
        {
            try
            {
                await CheckAndFireAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Daily digest tick failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Users whose digest fired on this tick (exposed for tests).</summary>
    public IReadOnlyList<Guid> LastTickFired { get; private set; } = [];

    private async Task CheckAndFireAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var candidates = await db.AppSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId != null && s.EmailEnabled && s.DailyDigestTime != null)
            .Select(s => new { UserId = s.UserId!.Value, s.TimeZone, DigestTime = s.DailyDigestTime!.Value })
            .ToListAsync(ct);

        var fired = new List<Guid>();
        foreach (var candidate in candidates)
        {
            // Each user's own wall clock (falls back to App:TimeZone for an unknown zone id).
            var now = _clockFactory.ForTimeZone(candidate.TimeZone).Now;
            var today = DateOnly.FromDateTime(now.DateTime);
            var currentTime = TimeOnly.FromDateTime(now.DateTime);

            if (_lastFiredDate.TryGetValue(candidate.UserId, out var last) && last == today)
                continue;
            if (currentTime.Hour != candidate.DigestTime.Hour || currentTime.Minute != candidate.DigestTime.Minute)
                continue;

            _lastFiredDate[candidate.UserId] = today;
            fired.Add(candidate.UserId);

            // TODO: build the actual digest body (today's blocks + due checklist items + due future
            // tasks, via IDayPlanBuilder in a scope acting as this user) and send it to the user's
            // confirmed email. Still a stub — sending is deliberately not switched on yet.
            _logger.LogInformation("Daily digest would fire now for user {UserId} ({Date}, {TimeZone})",
                candidate.UserId, today, candidate.TimeZone);
        }

        LastTickFired = fired;
    }
}
