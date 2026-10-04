using DayGrid.Domain.Enums;

namespace DayGrid.Domain.Entities;

/// <summary>
/// An explicit fire schedule for a <see cref="FutureTask"/> or a <see cref="ChecklistItem"/>.
/// Exactly one of <see cref="FutureTaskId"/> / <see cref="ChecklistItemId"/> must be set.
/// </summary>
public class Reminder
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? FutureTaskId { get; set; }
    public FutureTask? FutureTask { get; set; }

    public Guid? ChecklistItemId { get; set; }
    public ChecklistItem? ChecklistItem { get; set; }

    /// <summary>Minutes before the due moment. 10080 = 1 week.</summary>
    public int OffsetMinutes { get; set; }

    /// <summary>Computed on save; recomputed if the owning task/item moves.</summary>
    public DateTimeOffset FireAtUtc { get; set; }

    public NotificationChannel Channels { get; set; } = NotificationChannel.InApp;

    public ReminderStatus Status { get; set; } = ReminderStatus.Scheduled;

    public DateTimeOffset? SentAtUtc { get; set; }

    public int AttemptCount { get; set; }
}
