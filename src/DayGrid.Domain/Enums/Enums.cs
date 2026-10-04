namespace DayGrid.Domain.Enums;

/// <summary>Importance of a checklist item, future task or simple task.</summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

/// <summary>Outcome recorded for a single occurrence of a checklist item.</summary>
public enum CompletionStatus
{
    Done = 0,
    Skipped = 1,
    Partial = 2
}

/// <summary>How a checklist item is positioned in time.</summary>
public enum AnchorType
{
    Anytime = 0,
    FixedTime = 1,
    TimeWindow = 2,
    LinkedToBlock = 3
}

/// <summary>Category of a timetable block — also drives default colour.</summary>
public enum BlockCategory
{
    Work = 0,
    Personal = 1,
    Health = 2,
    Learning = 3,
    Break = 4,
    Sleep = 5,
    Other = 6
}

/// <summary>How a <see cref="Entities.TimetableAssignment"/> selects the dates it applies to.</summary>
public enum AssignmentScope
{
    Weekday = 0,
    SpecificDate = 1,
    DateRange = 2
}

/// <summary>What happens on a date that has a <see cref="Entities.DayOverride"/>.</summary>
public enum DayOverrideMode
{
    UseTemplate = 0,
    RestDay = 1,
    CustomOnly = 2
}

/// <summary>Lifecycle of a one-off <see cref="Entities.FutureTask"/>.</summary>
public enum FutureTaskStatus
{
    Pending = 0,
    Done = 1,
    Cancelled = 2,
    Deferred = 3
}

/// <summary>Lifecycle of a scheduled <see cref="Entities.Reminder"/>.</summary>
public enum ReminderStatus
{
    Scheduled = 0,
    Sent = 1,
    Failed = 2,
    Cancelled = 3
}

/// <summary>Delivery channels for a reminder. Stored as bit flags (smallint).</summary>
[Flags]
public enum NotificationChannel
{
    None = 0,
    InApp = 1,
    Email = 2
}

/// <summary>Status of an entry in the standalone Tasks module (<see cref="Entities.SimpleTask"/>).
/// Deliberately named <c>SimpleTaskStatus</c> rather than <c>TaskStatus</c> to avoid colliding
/// with <see cref="System.Threading.Tasks.TaskStatus"/>.</summary>
public enum SimpleTaskStatus
{
    Open = 0,
    Done = 1
}
