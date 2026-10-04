using DayGrid.Infrastructure.Time;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace DayGrid.UnitTests;

public class AppClockTests
{
    private static readonly TimeSpan Ist = TimeSpan.FromHours(5.5);

    private static AppClock ClockAt(DateTimeOffset utc, string? tz = "Asia/Kolkata") =>
        new(new FakeTimeProvider(utc), tz);

    [Fact]
    public void Now_IsExpressedInUserZone_WithIstOffset()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 6, 0, 0, TimeSpan.Zero));

        Assert.Equal(Ist, clock.Now.Offset);
        Assert.Equal(new DateTimeOffset(2026, 10, 4, 11, 30, 0, Ist), clock.Now);
        Assert.Equal(Ist, clock.TimeZone.BaseUtcOffset);
    }

    [Fact]
    public void JustBefore1830Utc_IsStillSameIstDate()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 18, 29, 59, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 4), clock.Today);
        Assert.Equal(new TimeOnly(23, 59, 59), clock.TimeOfDay);
    }

    [Fact]
    public void At1830Utc_RollsOverToNextIstDate()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 18, 30, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 5), clock.Today);
        Assert.Equal(new TimeOnly(0, 0, 0), clock.TimeOfDay);
    }

    [Fact]
    public void Today_FollowsTheTimeProvider_AsTimeAdvances()
    {
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 12, 31, 18, 0, 0, TimeSpan.Zero));
        var clock = new AppClock(fake, "Asia/Kolkata");

        Assert.Equal(new DateOnly(2026, 12, 31), clock.Today);
        Assert.Equal(new TimeOnly(23, 30), clock.TimeOfDay);

        fake.Advance(TimeSpan.FromMinutes(30));

        Assert.Equal(new DateOnly(2027, 1, 1), clock.Today);
        Assert.Equal(new TimeOnly(0, 0), clock.TimeOfDay);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlankZone_FallsBackToDefaultIst(string? tz)
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 18, 30, 0, TimeSpan.Zero), tz);

        Assert.Equal(Ist, clock.TimeZone.BaseUtcOffset);
        Assert.Equal(new DateOnly(2026, 10, 5), clock.Today);
    }

    [Fact]
    public void WindowsZoneId_IsAccepted()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 18, 30, 0, TimeSpan.Zero), "India Standard Time");

        Assert.Equal(Ist, clock.TimeZone.BaseUtcOffset);
        Assert.Equal(new DateOnly(2026, 10, 5), clock.Today);
    }

    [Fact]
    public void UtcZone_UsesUtcDate()
    {
        var clock = ClockAt(new DateTimeOffset(2026, 10, 4, 18, 30, 0, TimeSpan.Zero), "UTC");

        Assert.Equal(TimeSpan.Zero, clock.Now.Offset);
        Assert.Equal(new DateOnly(2026, 10, 4), clock.Today);
    }

    [Fact]
    public void InvalidZoneId_ThrowsTimeZoneNotFound_WithHelpfulMessage()
    {
        var ex = Assert.Throws<TimeZoneNotFoundException>(() => ClockAt(DateTimeOffset.UnixEpoch, "Mars/Olympus_Mons"));

        Assert.Contains("Mars/Olympus_Mons", ex.Message);
    }

    [Fact]
    public void NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AppClock(null!, "Asia/Kolkata"));
    }
}
