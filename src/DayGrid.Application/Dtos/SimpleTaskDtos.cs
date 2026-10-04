using DayGrid.Domain.Enums;

namespace DayGrid.Application.Dtos;

/// <summary>Wire shape for a standalone Tasks (simple_tasks) entry.</summary>
public sealed record SimpleTaskDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required Priority Priority { get; init; }
    public required SimpleTaskStatus Status { get; init; }
    public required int SortOrder { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record CreateSimpleTaskRequest
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public Priority? Priority { get; init; }
}

public sealed record UpdateSimpleTaskRequest
{
    public required string Title { get; init; }
    public string? Notes { get; init; }
    public required Priority Priority { get; init; }
}

public sealed record UpdateSimpleTaskStatusRequest
{
    public required SimpleTaskStatus Status { get; init; }
}

public sealed record ReorderItem
{
    public required Guid Id { get; init; }
    public required int SortOrder { get; init; }
}

/// <summary>Body for PUT .../reorder. Wraps the array the plan describes as `[{id, sortOrder}]`
/// as `{ "items": [...] }` so the request has a stable root object.</summary>
public sealed record ReorderRequest
{
    public List<ReorderItem> Items { get; init; } = new();
}
