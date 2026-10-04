using DayGrid.Application.Time;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.UnitTests;

/// <summary>An <see cref="IAppClock"/> frozen at a given user-local wall-clock time (IST, +05:30).</summary>
internal sealed class FixedAppClock : IAppClock
{
    public static readonly TimeSpan IstOffset = TimeSpan.FromHours(5.5);

    public FixedAppClock(DateTimeOffset now)
    {
        Now = now;
        TimeZone = TimeZoneInfo.CreateCustomTimeZone("Test/IST", IstOffset, "Test IST", "Test IST");
    }

    /// <summary>Clock at <paramref name="date"/> <paramref name="time"/> in a fixed +05:30 zone.</summary>
    public static FixedAppClock At(DateOnly date, TimeOnly time) =>
        new(new DateTimeOffset(date.ToDateTime(time), IstOffset));

    public TimeZoneInfo TimeZone { get; }
    public DateTimeOffset Now { get; }
    public DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Now.DateTime);
}

internal static class TestDb
{
    /// <summary>A fresh, isolated EF Core InMemory database. (SQLite in-memory can't host this
    /// model: the reminders check constraint uses Postgres-only '::int' casts.)</summary>
    public static AppDbContext Create(string? name = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name ?? Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }
}
