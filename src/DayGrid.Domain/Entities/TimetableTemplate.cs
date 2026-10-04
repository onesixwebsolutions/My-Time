using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>A named shape of a day: "Weekday", "Weekend", "Gym Day", "Travel".</summary>
public class TimetableTemplate : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Fallback template used when no assignment matches a date.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Rendering bounds for the timeline rail.</summary>
    public TimeOnly DayStart { get; set; } = new(6, 0);
    public TimeOnly DayEnd { get; set; } = new(23, 0);

    /// <summary>Grid granularity in minutes: 15/30/60.</summary>
    public short SlotMinutes { get; set; } = 30;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<TimetableBlock> Blocks { get; set; } = new List<TimetableBlock>();
    public ICollection<TimetableAssignment> Assignments { get; set; } = new List<TimetableAssignment>();
}
