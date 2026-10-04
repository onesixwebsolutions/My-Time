using DayGrid.Application.Scheduling;
using DayGrid.Domain.ValueObjects;
using Xunit;

namespace DayGrid.UnitTests;

public class RecurrenceEngineTests
{
    [Fact]
    public void None_NeverOccurs()
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.None };

        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 19)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 12, 25)));
    }

    [Theory]
    [InlineData(2026, 8, 17)]
    [InlineData(2026, 8, 18)]
    [InlineData(2026, 8, 19)]
    [InlineData(2026, 8, 25)]
    public void Daily_OccursEveryDay(int year, int month, int day)
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = new DateOnly(2026, 8, 1) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(year, month, day)));
    }

    [Fact]
    public void Daily_RespectsInterval()
    {
        // Every 2 days starting 2026-08-01: 1, 3, 5, 7 ...
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily, Interval = 2, StartDate = new DateOnly(2026, 8, 1) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 1)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 2)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 3)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 7)));
    }

    [Fact]
    public void Weekly_OnlyOnSpecifiedDaysOfWeek()
    {
        // Mon-Fri only.
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Weekly,
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
            StartDate = new DateOnly(2026, 8, 1)
        };

        // 2026-08-19 is a Wednesday.
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 19)));
        // 2026-08-22 is a Saturday.
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 22)));
        // 2026-08-23 is a Sunday.
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 23)));
    }

    [Fact]
    public void Weekly_RespectsIntervalOfTwoWeeks()
    {
        // Every other Monday, starting Monday 2026-08-03.
        var start = new DateOnly(2026, 8, 3);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);

        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Weekly,
            Interval = 2,
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday },
            StartDate = start
        };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 3)));   // week 0
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 10))); // week 1
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 17)));  // week 2
    }

    [Fact]
    public void EveryNDays_IntervalMath()
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.EveryNDays, Interval = 3, StartDate = new DateOnly(2026, 8, 1) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 1)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 2)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 3)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 4)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 7)));
    }

    [Fact]
    public void MonthlyByDay_ClampsToLastValidDayInShortMonth()
    {
        // "31st of every month" — February 2026 (not a leap year) has 28 days.
        var rule = new RecurrenceRule { Type = RecurrenceType.MonthlyByDay, DayOfMonth = 31, StartDate = new DateOnly(2026, 1, 1) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 2, 28)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 2, 27)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 1, 31)));
    }

    [Fact]
    public void MonthlyByDay_ClampsToLastValidDayInLeapYearFebruary()
    {
        // 2028 is a leap year — clamp should land on the 29th, not the 28th.
        var rule = new RecurrenceRule { Type = RecurrenceType.MonthlyByDay, DayOfMonth = 31, StartDate = new DateOnly(2026, 1, 1) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2028, 2, 29)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2028, 2, 28)));
    }

    [Fact]
    public void MonthlyByWeekday_FiresOnNthWeekdayOfMonth()
    {
        // 2nd Tuesday of August 2026 is the 11th.
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.MonthlyByWeekday,
            NthWeekday = new NthWeekday(2, DayOfWeek.Tuesday),
            StartDate = new DateOnly(2026, 1, 1)
        };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 11)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 4)));  // 1st Tuesday
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 18))); // 3rd Tuesday
    }

    [Fact]
    public void ExceptionDates_AlwaysReturnFalse()
    {
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Daily,
            StartDate = new DateOnly(2026, 8, 1),
            ExceptionDates = new List<DateOnly> { new(2026, 12, 25) }
        };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 12, 24)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 12, 25)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 12, 26)));
    }

    [Fact]
    public void StartDate_BeforeStartNeverOccurs()
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = new DateOnly(2026, 8, 10) };

        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 9)));
        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 10)));
    }

    [Fact]
    public void EndDate_AfterEndNeverOccurs()
    {
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Daily,
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 8, 15)
        };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 15)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 8, 16)));
    }

    [Fact]
    public void Expand_ReturnsEveryMatchingDateInRangeInclusive()
    {
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.Weekly,
            DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday },
            StartDate = new DateOnly(2026, 8, 1)
        };

        var dates = RecurrenceEngine.Expand(rule, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 14)).ToList();

        Assert.Equal(
            new[]
            {
                new DateOnly(2026, 8, 3),  // Mon
                new DateOnly(2026, 8, 7),  // Fri
                new DateOnly(2026, 8, 10), // Mon
                new DateOnly(2026, 8, 14)  // Fri
            },
            dates);
    }

    [Fact]
    public void Expand_EmptyWhenToBeforeFrom()
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = new DateOnly(2026, 8, 1) };

        var dates = RecurrenceEngine.Expand(rule, new DateOnly(2026, 8, 10), new DateOnly(2026, 8, 1)).ToList();

        Assert.Empty(dates);
    }
}
