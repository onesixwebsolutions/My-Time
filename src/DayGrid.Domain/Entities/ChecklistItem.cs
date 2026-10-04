using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;

using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// A to-do that belongs to a <see cref="Checklist"/>. This is where recurrence and time
/// anchoring live. Occurrences are computed on read via the RecurrenceEngine — this table
/// never stores one row per date.
/// </summary>
public class ChecklistItem : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public Guid ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public Priority Priority { get; set; } = Priority.Normal;

    /// <summary>Used to size the card on the timeline; not a hard constraint.</summary>
    public int? EstimatedMinutes { get; set; }

    public AnchorType AnchorType { get; set; } = AnchorType.Anytime;

    /// <summary>Set when <see cref="AnchorType"/> is FixedTime.</summary>
    public TimeOnly? AnchorTime { get; set; }

    /// <summary>Set when <see cref="AnchorType"/> is TimeWindow.</summary>
    public TimeOnly? WindowStart { get; set; }
    public TimeOnly? WindowEnd { get; set; }

    /// <summary>Set when <see cref="AnchorType"/> is LinkedToBlock — item rides along with a timetable block.</summary>
    public Guid? TimetableBlockId { get; set; }

    /// <summary>Recurrence rule; a default value (Type = None) means the item is a one-off tied to <see cref="DueDate"/>.</summary>
    public RecurrenceRule Recurrence { get; set; } = new();

    /// <summary>Only meaningful when <see cref="Recurrence"/>'s Type is None.</summary>
    public DateOnly? DueDate { get; set; }

    /// <summary>Fire a reminder N minutes before <see cref="AnchorTime"/>, if set.</summary>
    public int? ReminderOffsetMinutes { get; set; }

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<ChecklistCompletion> Completions { get; set; } = new List<ChecklistCompletion>();
}
