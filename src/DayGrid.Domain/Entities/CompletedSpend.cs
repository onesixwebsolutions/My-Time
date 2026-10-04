using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// A running log of money actually spent, entered as you go — distinct from
/// <see cref="VaryingExpense"/> (which can be logged for any date/month) in that this is
/// specifically "what I've completed spending this month," surfaced on the Expenses page as its
/// own list + its own "My Spends Completed This Month" total, separate from the
/// constant/varying totals.
/// </summary>
public class CompletedSpend : IUserOwned
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
