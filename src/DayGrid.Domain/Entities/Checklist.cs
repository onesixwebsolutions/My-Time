namespace DayGrid.Domain.Entities;

/// <summary>A grouping container for checklist items — "Morning Routine", "Work", "Health".</summary>
public class Checklist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Hex colour, e.g. "#f59e0b" — drives the UI accent.</summary>
    public string? Color { get; set; }

    /// <summary>Lucide icon name.</summary>
    public string? Icon { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Soft delete — keeps completion history intact.</summary>
    public bool IsArchived { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChecklistItem> Items { get; set; } = new List<ChecklistItem>();
}
