using DayGrid.Api.Auth;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Time;
using DayGrid.TestSupport;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace DayGrid.IntegrationTests.Infrastructure;

/// <summary>
/// Boots the real Program in External mode against the embedded test server. The DbContext is
/// the app's own Npgsql registration (nothing swapped), background services are removed, the
/// user clock is frozen and outgoing email is captured.
/// </summary>
public class IntegrationApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Frozen "now": Monday 2026-10-05 10:30 IST (+05:30).</summary>
    public static readonly DateTimeOffset FrozenNow = new(2026, 10, 5, 10, 30, 0, TimeSpan.FromHours(5.5));

    private readonly string _connectionString;

    public FakeEmailSender Email { get; } = new();

    public IntegrationApiFactory(string connectionString)
    {
        var port = new NpgsqlConnectionStringBuilder(connectionString).Port;
        if (port == 5432)
            throw new InvalidOperationException("Integration tests must never target port 5432.");
        _connectionString = connectionString;
        Email.SettleWith(() => Services.GetRequiredService<AccountEmailQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(10)));

        // Program.cs reads Database:Mode and the connection string from builder.Configuration
        // before WebApplicationFactory's configuration hooks apply; environment variables are
        // visible at that point (same approach as DayGrid.Api.Tests).
        Environment.SetEnvironmentVariable("Database__Mode", "External");
        Environment.SetEnvironmentVariable("Database__InitializeSchema", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", connectionString);
        Environment.SetEnvironmentVariable("App__TimeZone", "Asia/Kolkata");
        Environment.SetEnvironmentVariable("Serilog__MinimumLevel__Default", "Warning");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Mode", "External");
        builder.UseSetting("Database:InitializeSchema", "false");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("RateLimiting:Auth:PermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("Auth:SecurityStampValidationIntervalSeconds", "0");
        builder.UseSetting("App:PublicBaseUrl", "https://daygrid.test");
        builder.UseSetting("Email:AccountEmails:CooldownSeconds", "0");
        builder.UseSetting("Email:AccountEmails:DailyLimitPerRecipient", "0");
        ConfigureSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            foreach (var d in services
                         .Where(d => d.ServiceType == typeof(IHostedService)
                                     && (d.ImplementationType == typeof(ReminderDispatcherService)
                                         || d.ImplementationType == typeof(DailyDigestService)))
                         .ToList())
                services.Remove(d);

            foreach (var d in services.Where(d => d.ServiceType == typeof(IAppClockFactory) || d.ServiceType == typeof(IEmailSender)).ToList())
                services.Remove(d);
            // Only users' wall clocks are frozen (default zone Asia/Kolkata); auth keeps real time.
            services.AddSingleton<IAppClockFactory>(new AppClockFactory(new FrozenTimeProvider(FrozenNow), "Asia/Kolkata"));
            services.AddSingleton<IEmailSender>(Email);
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000);

            // As in Development / the desktop exe: the first account to confirm its email becomes
            // Admin and claims the legacy rows (the default test user is that account).
            foreach (var d in services.Where(d => d.ServiceType == typeof(BootstrapAdminPolicy)).ToList())
                services.Remove(d);
            services.AddSingleton(BootstrapPolicy());
        });
    }

    /// <summary>The bootstrap-Admin policy for this host (tests of the other modes override it).</summary>
    protected virtual BootstrapAdminPolicy BootstrapPolicy() => BootstrapAdminPolicy.FirstConfirmedUser();

    protected virtual int AuthPermitLimit => 100_000;

    protected virtual void ConfigureSettings(IWebHostBuilder builder)
    {
    }
}
