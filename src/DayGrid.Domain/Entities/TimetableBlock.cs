using DayGrid.Domain.Enums;

namespace DayGrid.Domain.Entities;

/// <summary>An hour/minute row inside a <see cref="TimetableTemplate"/>.</summary>
public class TimetableBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TemplateId { get; set; }
    public TimetableTemplate? Template { get; set; }

    public string Title { get; set; } = string.Empty;

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public BlockCategory Category { get; set; } = BlockCategory.Other;

    /// <summary>Overrides the category's default colour, e.g. "#3b82f6".</summary>
    public string? Color { get; set; }

    public string? Location { get; set; }

    /// <summary>When set, surfaces that checklist's items inside this block on the Today view.</summary>
    public Guid? ChecklistId { get; set; }

    /// <summary>Validation escape hatch — allows this block to overlap siblings.</summary>
    public bool AllowOverlap { get; set; }

    public bool NotifyAtStart { get; set; }

    /// <summary>Tie-break for blocks that share the same StartTime.</summary>
    public int SortOrder { get; set; }
}
