using System.Text.Json;
using DayGrid.Application.Scheduling;
using DayGrid.Domain.Entities;
using DayGrid.Domain.ValueObjects;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DayGrid.UnitTests;

public class RecurrenceRuleTests
{
    [Fact]
    public void Default_IsNonRecurringOneOff()
    {
        var rule = new RecurrenceRule();

        Assert.Equal(RecurrenceType.None, rule.Type);
        Assert.Equal(1, rule.Interval);
        Assert.Empty(rule.DaysOfWeek);
        Assert.Empty(rule.ExceptionDates);
        Assert.Null(rule.StartDate);
        Assert.Null(rule.EndDate);
        Assert.Null(rule.NthWeekday);
    }

    [Fact]
    public void JsonRoundTrip_WebDefaults_PreservesAllFields()
    {
        // Same options ChecklistItemConfiguration uses for the jsonb column.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var rule = new RecurrenceRule
        {
            Type = RecurrenceType.MonthlyByWeekday,
            Interval = 2,
            DaysOfWeek = [DayOfWeek.Monday, DayOfWeek.Friday],
            DayOfMonth = 31,
            NthWeekday = new NthWeekday(-1, DayOfWeek.Friday),
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ExceptionDates = [new DateOnly(2026, 5, 29)]
        };

        var back = JsonSerializer.Deserialize<RecurrenceRule>(JsonSerializer.Serialize(rule, options), options)!;

        Assert.Equal(rule.Type, back.Type);
        Assert.Equal(rule.Interval, back.Interval);
        Assert.Equal(rule.DaysOfWeek, back.DaysOfWeek);
        Assert.Equal(rule.DayOfMonth, back.DayOfMonth);
        Assert.Equal(rule.NthWeekday, back.NthWeekday);
        Assert.Equal(rule.StartDate, back.StartDate);
        Assert.Equal(rule.EndDate, back.EndDate);
        Assert.Equal(rule.ExceptionDates, back.ExceptionDates);
    }

    [Fact]
    public async Task ChecklistItemRecurrence_PersistsThroughEfValueConverter()
    {
        var dbName = Guid.NewGuid().ToString();
        var rule = new RecurrenceRule { Type = RecurrenceType.Weekly, DaysOfWeek = [DayOfWeek.Tuesday], ExceptionDates = [new DateOnly(2026, 10, 6)] };
        var checklist = new Checklist { Name = "c" };
        var item = new ChecklistItem { Title = "i", Recurrence = rule };
        checklist.Items.Add(item);

        using (var db = TestDb.Create(dbName))
        {
            db.Checklists.Add(checklist);
            await db.SaveChangesAsync();
        }

        using (var db = TestDb.Create(dbName))
        {
            var loaded = await db.ChecklistItems.AsNoTracking().SingleAsync();
            Assert.Equal(RecurrenceType.Weekly, loaded.Recurrence.Type);
            Assert.Equal([DayOfWeek.Tuesday], loaded.Recurrence.DaysOfWeek);
            Assert.False(RecurrenceEngine.Occurs(loaded.Recurrence, new DateOnly(2026, 10, 6)));
            Assert.True(RecurrenceEngine.Occurs(loaded.Recurrence, new DateOnly(2026, 10, 13)));
        }
    }

    [Theory]
    [InlineData(31, 2026, 2, 28)] // clamps to last day of a short month
    [InlineData(31, 2028, 2, 29)] // leap year
    [InlineData(30, 2026, 4, 30)]
    public void MonthlyByDay_ClampsToLastDayOfMonth(int dayOfMonth, int y, int m, int d)
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.MonthlyByDay, DayOfMonth = dayOfMonth };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(y, m, d)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(y, m, d).AddDays(-1)));
    }

    [Fact]
    public void MonthlyByWeekday_NegativeNth_MeansFromEnd()
    {
        // Last Friday of October 2026 is the 30th.
        var rule = new RecurrenceRule { Type = RecurrenceType.MonthlyByWeekday, NthWeekday = new NthWeekday(-1, DayOfWeek.Friday) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 10, 30)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 10, 23)));
    }

    [Fact]
    public void EndDate_IsInclusive()
    {
        var rule = new RecurrenceRule { Type = RecurrenceType.Daily, EndDate = new DateOnly(2026, 10, 5) };

        Assert.True(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 10, 5)));
        Assert.False(RecurrenceEngine.Occurs(rule, new DateOnly(2026, 10, 6)));
    }
}
