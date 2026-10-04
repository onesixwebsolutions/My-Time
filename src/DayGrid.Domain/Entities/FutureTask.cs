using DayGrid.Domain.Enums;

namespace DayGrid.Domain.Entities;

/// <summary>A one-off thing on a future date that must produce a notification.</summary>
public class FutureTask
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public DateOnly DueDate { get; set; }

    /// <summary>Null = all-day.</summary>
    public TimeOnly? DueTime { get; set; }

    public string? Category { get; set; }
    public Priority Priority { get; set; } = Priority.Normal;

    public FutureTaskStatus Status { get; set; } = FutureTaskStatus.Pending;
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>On the due date this task also appears in the referenced checklist's column.</summary>
    public Guid? PromoteToChecklistId { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<Reminder> Reminders { get; set; } = new List<Reminder>();
}
