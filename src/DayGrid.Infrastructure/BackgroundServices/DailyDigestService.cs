using DayGrid.Application.Time;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DayGrid.Infrastructure.BackgroundServices;

/// <summary>
/// Checks <c>app_settings.daily_digest_time</c> once a minute and, when the current wall-clock
/// time matches, triggers the "here's your day" email. Guards against firing twice in the same
/// minute (and twice in the same day, once the underlying DB is wired up for it) via
/// <see cref="_lastFiredDate"/>.
/// </summary>
public class DailyDigestService : BackgroundService
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DailyDigestService> _logger;
    private readonly IAppClock _clock;
    private DateOnly? _lastFiredDate;

    public DailyDigestService(IServiceScopeFactory scopeFactory, ILogger<DailyDigestService> logger, IAppClock clock)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _clock = clock;
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

    private async Task CheckAndFireAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        if (settings?.DailyDigestTime is not { } digestTime || !settings.EmailEnabled)
            return;

        // User-local wall clock (App:TimeZone), not the server's zone — UTC on Azure.
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var currentTime = TimeOnly.FromDateTime(now.DateTime);

        // Fire once per day, on the minute the configured digest time falls in.
        if (_lastFiredDate == today)
            return;

        if (currentTime.Hour != digestTime.Hour || currentTime.Minute != digestTime.Minute)
            return;

        _lastFiredDate = today;

        // TODO: build the actual digest body (today's blocks + due checklist items + due future
        // tasks, reusing IDayPlanBuilder) and send it via IEmailSender to settings.EmailTo. Left
        // as a stub for Phase 6 — the scheduling/trigger logic above is the part worth getting
        // right first; the email template is comparatively mechanical.
        _logger.LogInformation("Daily digest would fire now for {Date} (email body not yet implemented)", today);
    }
}
