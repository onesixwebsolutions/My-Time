namespace DayGrid.Domain.Entities;

/// <summary>
/// A fixed, recurring monthly expense — rent, a subscription, insurance, an EMI. Amount is the
/// same every month; DayOfMonth is when in the month it's due (informational only, no reminder
/// wiring). IsActive lets a subscription be paused/cancelled without losing its history — an
/// inactive entry is excluded from the monthly total but stays in the list.
/// </summary>
public class ConstantExpense
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string? Category { get; set; }

    /// <summary>1–31. Null means "no fixed day" (e.g. an EMI that varies by bank processing).</summary>
    public int? DayOfMonth { get; set; }

    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
