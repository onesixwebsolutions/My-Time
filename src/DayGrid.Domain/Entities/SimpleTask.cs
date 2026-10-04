using DayGrid.Domain.Enums;

using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// The standalone Tasks module entity — a private scratch list. Deliberately kept
/// dependency-free: it must never reference, or be referenced by, any other entity in the
/// domain. It has no recurrence, no time anchor, no reminder, and is never pulled into the
/// <c>/today</c> aggregate. If an entry here later needs a date and a reminder, the move is
/// to create a <see cref="FutureTask"/> or a <see cref="ChecklistItem"/> instead — this entity
/// stays "things I'm keeping track of, nothing more."
/// </summary>
public class SimpleTask : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public string? Notes { get; set; }

    public Priority Priority { get; set; } = Priority.Normal;

    public SimpleTaskStatus Status { get; set; } = SimpleTaskStatus.Open;

    /// <summary>Manual drag ordering within the section.</summary>
    public int SortOrder { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
