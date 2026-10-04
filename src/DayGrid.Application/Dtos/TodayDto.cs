namespace DayGrid.Application.Dtos;

// NOTE: Program.cs configures System.Text.Json with JsonSerializerOptions.PropertyNamingPolicy =
// JsonNamingPolicy.CamelCase globally, so these records rely on the default PascalCase-to-camelCase
// mapping and do not need per-property [JsonPropertyName] attributes.

/// <summary>Full response for GET /api/v1/today — everything the home page needs in one call.</summary>
public sealed record TodayDto
{
    public required DateOnly Date { get; init; }
    public required string DayOfWeek { get; init; }
    public required string DisplayDate { get; init; }

    public DayOverrideDto? Override { get; init; }
    public required TemplateSummaryDto Template { get; init; }

    public NowBlockDto? NowBlock { get; init; }
    public NextBlockDto? NextBlock { get; init; }

    public required List<TimetableBlockDto> Blocks { get; init; }
    public required List<ChecklistGroupDto> Checklists { get; init; }

    public required List<DueTaskDto> DueToday { get; init; }
    public required List<DueTaskDto> Overdue { get; init; }

    public required DaySummaryDto Summary { get; init; }
}

public sealed record DayOverrideDto
{
    public required string Mode { get; init; }
    public string? Note { get; init; }
}

public sealed record TemplateSummaryDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required TimeOnly DayStart { get; init; }
    public required TimeOnly DayEnd { get; init; }
    public required short SlotMinutes { get; init; }
}

/// <summary>What you should be doing THIS minute.</summary>
public sealed record NowBlockDto
{
    public required Guid BlockId { get; init; }
    public required string Title { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }
    public required string Category { get; init; }
    public string? Color { get; init; }
    public required int ElapsedMinutes { get; init; }
    public required int RemainingMinutes { get; init; }
    public required int ProgressPercent { get; init; }
}

public sealed record NextBlockDto
{
    public required Guid BlockId { get; init; }
    public required string Title { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required int StartsInMinutes { get; init; }
}

public sealed record LinkedItemDto
{
    public required Guid ItemId { get; init; }
    public required string Title { get; init; }
    public required bool IsCompleted { get; init; }
}

public sealed record TimetableBlockDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required TimeOnly StartTime { get; init; }
    public required TimeOnly EndTime { get; init; }
    public required string Category { get; init; }
    public string? Color { get; init; }
    public string? Location { get; init; }

    /// <summary>"past" | "current" | "upcoming".</summary>
    public required string State { get; init; }

    public required List<LinkedItemDto> LinkedItems { get; init; }
}

public sealed record ChecklistItemDto
{
    public required Guid ItemId { get; init; }
    public required string Title { get; init; }
    public required string AnchorType { get; init; }
    public TimeOnly? AnchorTime { get; init; }
    public required string Priority { get; init; }
    public int? EstimatedMinutes { get; init; }
    public required bool IsCompleted { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public required bool IsOverdue { get; init; }
    public string? RecurrenceLabel { get; init; }
}

public sealed record ChecklistGroupDto
{
    public required Guid ChecklistId { get; init; }
    public required string Name { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
    public required int CompletedCount { get; init; }
    public required int TotalCount { get; init; }
    public required List<ChecklistItemDto> Items { get; init; }
}

public sealed record DueTaskDto
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public TimeOnly? DueTime { get; init; }
    public required string Priority { get; init; }
}

public sealed record DaySummaryDto
{
    public required int TotalItems { get; init; }
    public required int CompletedItems { get; init; }
    public required int CompletionPercent { get; init; }
    public required int BlocksTotal { get; init; }
    public required int BlocksDone { get; init; }
    public required int MinutesScheduled { get; init; }
}
