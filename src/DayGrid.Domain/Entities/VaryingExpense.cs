using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// A one-off or irregular expense tied to a specific date — groceries, dining out, a repair.
/// Unlike <see cref="ConstantExpense"/> this has no recurrence; each entry is its own occurrence.
/// </summary>
public class VaryingExpense : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }
    public DateOnly Date { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
