using DayGrid.Domain.ValueObjects;

namespace DayGrid.Application.Scheduling;

/// <summary>
/// Pure functions that interpret a <see cref="RecurrenceRule"/>. No DB access, no
/// <see cref="DateTime.Now"/>/<see cref="DateTime.Today"/> calls — dates are always passed in
/// as parameters, which is what makes this fully unit-testable.
/// </summary>
public static class RecurrenceEngine
{
    /// <summary>True if <paramref name="rule"/> fires on <paramref name="date"/>.</summary>
    public static bool Occurs(RecurrenceRule rule, DateOnly date)
    {
        // An exception date always wins, regardless of what the rule would otherwise say.
        if (rule.ExceptionDates.Contains(date))
            return false;

        if (rule.StartDate is { } start && date < start)
            return false;

        if (rule.EndDate is { } end && date > end)
            return false;

        return rule.Type switch
        {
            RecurrenceType.None => false,
            RecurrenceType.Daily => OccursDaily(rule, date),
            RecurrenceType.Weekly => OccursWeekly(rule, date),
            RecurrenceType.MonthlyByDay => OccursMonthlyByDay(rule, date),
            RecurrenceType.MonthlyByWeekday => OccursMonthlyByWeekday(rule, date),
            RecurrenceType.EveryNDays => OccursEveryNDays(rule, date),
            // TODO: Custom currently reuses the Weekly interpretation (DaysOfWeek + Interval-in-weeks).
            // A real "Custom" builder (e.g. combining multiple rules, or RRULE-style BYSETPOS) is out of
            // scope for this scaffold; revisit once a concrete use case shows up.
            RecurrenceType.Custom => OccursWeekly(rule, date),
            _ => false
        };
    }

    /// <summary>Every date in [<paramref name="from"/>, <paramref name="to"/>] (inclusive) where <see cref="Occurs"/> is true.</summary>
    public static IEnumerable<DateOnly> Expand(RecurrenceRule rule, DateOnly from, DateOnly to)
    {
        if (to < from)
            yield break;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            if (Occurs(rule, date))
                yield return date;
        }
    }

    private static bool OccursDaily(RecurrenceRule rule, DateOnly date)
    {
        var interval = Math.Max(rule.Interval, 1);
        if (interval == 1)
            return true;

        var reference = rule.StartDate ?? DateOnly.MinValue;
        var diff = date.DayNumber - reference.DayNumber;
        return diff >= 0 && diff % interval == 0;
    }

    private static bool OccursWeekly(RecurrenceRule rule, DateOnly date)
    {
        if (rule.DaysOfWeek.Count == 0 || !rule.DaysOfWeek.Contains(date.DayOfWeek))
            return false;

        var interval = Math.Max(rule.Interval, 1);
        if (interval == 1)
            return true;

        var reference = rule.StartDate ?? date;
        var weeksDiff = (StartOfWeek(date).DayNumber - StartOfWeek(reference).DayNumber) / 7;
        return weeksDiff >= 0 && weeksDiff % interval == 0;
    }

    private static bool OccursEveryNDays(RecurrenceRule rule, DateOnly date)
    {
        var interval = Math.Max(rule.Interval, 1);
        var reference = rule.StartDate ?? DateOnly.MinValue;
        var diff = date.DayNumber - reference.DayNumber;
        return diff >= 0 && diff % interval == 0;
    }

    private static bool OccursMonthlyByDay(RecurrenceRule rule, DateOnly date)
    {
        if (rule.DayOfMonth is not { } dayOfMonth || dayOfMonth < 1)
            return false;

        // Policy: clamp to the last valid day of the month (e.g. "31st" fires on the 28th/29th in February).
        var lastDayOfMonth = DateTime.DaysInMonth(date.Year, date.Month);
        var clamped = Math.Min(dayOfMonth, lastDayOfMonth);
        return date.Day == clamped;
    }

    private static bool OccursMonthlyByWeekday(RecurrenceRule rule, DateOnly date)
    {
        if (rule.NthWeekday is not { } nth)
            return false;

        var occurrence = NthWeekdayOfMonth(date.Year, date.Month, nth.DayOfWeek, nth.Nth);
        return occurrence == date;
    }

    /// <summary>Nth (1-based; negative counts from the end, e.g. -1 = last) weekday of a given month, if it exists.</summary>
    private static DateOnly? NthWeekdayOfMonth(int year, int month, DayOfWeek dayOfWeek, int nth)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (nth > 0)
        {
            var first = new DateOnly(year, month, 1);
            var offset = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
            var day = 1 + offset + (nth - 1) * 7;
            return day <= daysInMonth ? new DateOnly(year, month, day) : null;
        }

        if (nth < 0)
        {
            var last = new DateOnly(year, month, daysInMonth);
            var offset = ((int)last.DayOfWeek - (int)dayOfWeek + 7) % 7;
            var day = daysInMonth - offset + (nth + 1) * 7;
            return day >= 1 ? new DateOnly(year, month, day) : null;
        }

        return null;
    }

    private static DateOnly StartOfWeek(DateOnly date)
    {
        // Sunday-pivoted week boundary. Good enough for "every N weeks" interval math; the app's
        // configurable week-start (AppSetting.WeekStartsOn) only affects UI layout, not this calculation.
        var diff = (int)date.DayOfWeek;
        return date.AddDays(-diff);
    }
}
