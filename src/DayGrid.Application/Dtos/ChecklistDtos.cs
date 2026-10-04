using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;

namespace DayGrid.Application.Dtos;

public sealed record ChecklistDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public required int SortOrder { get; init; }
    public required bool IsArchived { get; init; }
    public required int ItemCount { get; init; }
    public required int CompletedTodayCount { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateChecklistRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
}

public sealed record UpdateChecklistRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public int SortOrder { get; init; }
}

/// <summary>Mirrors the "Create item request" example in plan section 5.3.</summary>
public sealed record CreateChecklistItemRequest
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public Priority Priority { get; init; } = Priority.Normal;
    public int? EstimatedMinutes { get; init; }
    public AnchorType AnchorType { get; init; } = AnchorType.Anytime;
    public TimeOnly? AnchorTime { get; init; }
    public TimeOnly? WindowStart { get; init; }
    public TimeOnly? WindowEnd { get; init; }
    public Guid? TimetableBlockId { get; init; }
    public RecurrenceRule Recurrence { get; init; } = new();
    public DateOnly? DueDate { get; init; }
    public int? ReminderOffsetMinutes { get; init; }
}

public sealed record UpdateChecklistItemRequest
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public Priority Priority { get; init; } = Priority.Normal;
    public int? EstimatedMinutes { get; init; }
    public AnchorType AnchorType { get; init; } = AnchorType.Anytime;
    public TimeOnly? AnchorTime { get; init; }
    public TimeOnly? WindowStart { get; init; }
    public TimeOnly? WindowEnd { get; init; }
    public Guid? TimetableBlockId { get; init; }
    public RecurrenceRule Recurrence { get; init; } = new();
    public DateOnly? DueDate { get; init; }
    public int? ReminderOffsetMinutes { get; init; }
    public bool IsActive { get; init; } = true;
}

/// <summary>Body for POST /items/{itemId}/complete.</summary>
public sealed record CompleteItemRequest
{
    public required DateOnly Date { get; init; }
    public CompletionStatus Status { get; init; } = CompletionStatus.Done;
    public string? Note { get; init; }
}

/// <summary>Body for POST /items/{itemId}/skip.</summary>
public sealed record SkipItemRequest
{
    public required DateOnly Date { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Body for POST /items/preview-occurrences — a RecurrenceRule plus a date range.</summary>
public sealed record PreviewOccurrencesRequest
{
    public required RecurrenceRule Recurrence { get; init; }
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
}

public sealed record ChecklistItemHistoryEntryDto
{
    public required DateOnly OccurrenceDate { get; init; }
    public required CompletionStatus Status { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public string? Note { get; init; }
}
