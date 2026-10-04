using DayGrid.Domain.Enums;

namespace DayGrid.Domain.Entities;

/// <summary>
/// One row per tick of a <see cref="ChecklistItem"/> occurrence. Absence of a row for a
/// given (ChecklistItemId, OccurrenceDate) means the item is not done for that date.
/// </summary>
public class ChecklistCompletion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ChecklistItemId { get; set; }
    public ChecklistItem? ChecklistItem { get; set; }

    /// <summary>The date the occurrence was owed (not necessarily the date it was actually ticked).</summary>
    public DateOnly OccurrenceDate { get; set; }

    public DateTimeOffset CompletedAt { get; set; } = DateTimeOffset.UtcNow;

    public CompletionStatus Status { get; set; } = CompletionStatus.Done;

    public string? Note { get; set; }
}
