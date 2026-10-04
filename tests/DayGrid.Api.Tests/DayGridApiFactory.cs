using DayGrid.Application.Time;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Time;
using DayGrid.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DayGrid.Api.Tests;

/// <summary>
/// Boots the real Program with no Postgres: External mode + a dummy connection string (never
/// opened), schema migration off, AppDbContext swapped for EF InMemory, background services
/// removed, user clocks frozen, outgoing email captured and the auth rate limit raised (one test
/// class signs in many users). Authentication itself is the real cookie + antiforgery pipeline.
/// </summary>
public class DayGridApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Frozen "now" for user clocks: Monday 2026-10-05 10:30 IST (+05:30).</summary>
    public static readonly DateTimeOffset FrozenNow = new(2026, 10, 5, 10, 30, 0, TimeSpan.FromHours(5.5));

    private readonly string _dbName = "daygrid-api-tests-" + Guid.NewGuid();

    public FakeEmailSender Email { get; } = new();

    static DayGridApiFactory()
    {
        // Program.cs reads Database:Mode / connection string from builder.Configuration *before*
        // Build(), which is earlier than WebApplicationFactory's ConfigureAppConfiguration hooks
        // apply. Environment variables are part of the default configuration sources, so they are
        // visible at that point (and override appsettings.json's "Embedded").
        Environment.SetEnvironmentVariable("Database__Mode", "External");
        Environment.SetEnvironmentVariable("Database__InitializeSchema", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none");
        Environment.SetEnvironmentVariable("App__TimeZone", "Asia/Kolkata");
    }

    /// <summary>Auth rate limit (requests per window per client IP) for this host.</summary>
    protected virtual int AuthPermitLimit => 100_000;

    protected virtual void ConfigureSettings(IWebHostBuilder builder)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Mode", "External");
        builder.UseSetting("Database:InitializeSchema", "false");
        builder.UseSetting("RateLimiting:Auth:PermitLimit", AuthPermitLimit.ToString());
        builder.UseSetting("Auth:SecurityStampValidationIntervalSeconds", "0"); // revocation is immediate in tests
        builder.UseSetting("App:PublicBaseUrl", "https://daygrid.test");
        ConfigureSettings(builder);

        builder.ConfigureTestServices(services =>
        {
            // Replace the Npgsql-backed AppDbContext with InMemory.
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<AppDbContext>();
            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));

            // Background services would poll the database on startup.
            var hosted = services
                .Where(d => d.ServiceType == typeof(IHostedService)
                            && (d.ImplementationType == typeof(ReminderDispatcherService)
                                || d.ImplementationType == typeof(DailyDigestService)))
                .ToList();
            foreach (var d in hosted)
                services.Remove(d);

            // Only the users' wall clocks are frozen; auth cookies/tokens keep real time.
            services.RemoveAll<IAppClockFactory>();
            services.AddSingleton<IAppClockFactory>(new AppClockFactory(new FrozenTimeProvider(FrozenNow), "Asia/Kolkata"));

            ReplaceEmailSender(services);

            // Cheap password hashing: every test signs a user in.
            services.Configure<PasswordHasherOptions>(o => o.IterationCount = 1_000);
        });
    }

    protected virtual void ReplaceEmailSender(IServiceCollection services)
    {
        services.RemoveAll<IEmailSender>();
        services.AddSingleton<IEmailSender>(Email);
    }
}

internal static class ServiceCollectionTestExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
            services.Remove(d);
    }
}
