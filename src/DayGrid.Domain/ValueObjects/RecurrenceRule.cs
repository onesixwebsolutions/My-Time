namespace DayGrid.Domain.ValueObjects;

/// <summary>The kind of recurrence a <see cref="RecurrenceRule"/> describes.</summary>
public enum RecurrenceType
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    MonthlyByDay = 3,
    MonthlyByWeekday = 4,
    EveryNDays = 5,
    Custom = 6
}

/// <summary>
/// Describes the Nth weekday of a month, e.g. { Nth = 2, DayOfWeek = Tuesday } = "2nd Tuesday".
/// Nth may be negative to mean "from the end" (e.g. -1 = last Friday), though the default
/// <see cref="Scheduling.RecurrenceEngine"/> implementation only needs to support positive Nth.
/// </summary>
public sealed record NthWeekday(int Nth, DayOfWeek DayOfWeek);

/// <summary>
/// A recurrence rule, stored as JSONB on <see cref="Entities.ChecklistItem"/>. Pure data —
/// all interpretation happens in <c>DayGrid.Application.Scheduling.RecurrenceEngine</c>.
/// A default-constructed instance (Type = None) represents a one-off, non-recurring item.
/// </summary>
public sealed record RecurrenceRule
{
    /// <summary>Kind of recurrence. <see cref="RecurrenceType.None"/> means "does not repeat".</summary>
    public RecurrenceType Type { get; init; } = RecurrenceType.None;

    /// <summary>Interval multiplier — every N days/weeks/months, depending on <see cref="Type"/>. Default 1.</summary>
    public int Interval { get; init; } = 1;

    /// <summary>Days of week the rule fires on. Used by <see cref="RecurrenceType.Weekly"/> (and Custom).</summary>
    public List<DayOfWeek> DaysOfWeek { get; init; } = new();

    /// <summary>Day-of-month for <see cref="RecurrenceType.MonthlyByDay"/>. Clamped to the last valid day of short months.</summary>
    public int? DayOfMonth { get; init; }

    /// <summary>Nth weekday of the month for <see cref="RecurrenceType.MonthlyByWeekday"/> (e.g. 2nd Tuesday).</summary>
    public NthWeekday? NthWeekday { get; init; }

    /// <summary>First date the rule can fire on. Null = unbounded in the past.</summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>Last date the rule can fire on (inclusive). Null = unbounded in the future.</summary>
    public DateOnly? EndDate { get; init; }

    /// <summary>Specific dates that are always excluded, regardless of what the rule would otherwise say.</summary>
    public List<DateOnly> ExceptionDates { get; init; } = new();
}
