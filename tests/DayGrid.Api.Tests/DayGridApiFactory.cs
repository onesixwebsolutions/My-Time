using DayGrid.Application.Time;
using DayGrid.Infrastructure.BackgroundServices;
using DayGrid.Infrastructure.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DayGrid.Api.Tests;

/// <summary>
/// Boots the real Program with no Postgres: External mode + a dummy connection string (never
/// opened), schema bootstrap off, AppDbContext swapped for EF InMemory, background services
/// removed, and the user clock frozen.
/// </summary>
public sealed class DayGridApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Frozen "now" for the API: Monday 2026-10-05 10:30 IST (+05:30).</summary>
    public static readonly DateTimeOffset FrozenNow = new(2026, 10, 5, 10, 30, 0, TimeSpan.FromHours(5.5));

    private readonly string _dbName = "daygrid-api-tests-" + Guid.NewGuid();

    static DayGridApiFactory()
    {
        // Program.cs reads Database:Mode / connection string from builder.Configuration *before*
        // Build(), which is earlier than WebApplicationFactory's ConfigureAppConfiguration hooks
        // apply. Environment variables are part of the default configuration sources, so they are
        // visible at that point (and override appsettings.json's "Embedded").
        Environment.SetEnvironmentVariable("Database__Mode", "External");
        Environment.SetEnvironmentVariable("Database__InitializeSchema", "false");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", "Host=127.0.0.1;Port=1;Database=none;Username=none;Password=none");
        Environment.SetEnvironmentVariable("App__TimeZone", "UTC");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Mode", "External");
        builder.UseSetting("Database:InitializeSchema", "false");

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

            services.RemoveAll<IAppClock>();
            services.AddSingleton<IAppClock>(new FixedAppClock(FrozenNow));
        });
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

internal sealed class FixedAppClock : IAppClock
{
    public FixedAppClock(DateTimeOffset now)
    {
        Now = now;
        TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/IST", now.Offset, "Test IST", "Test IST");
    }

    public TimeZoneInfo TimeZone { get; }
    public DateTimeOffset Now { get; }
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
}
