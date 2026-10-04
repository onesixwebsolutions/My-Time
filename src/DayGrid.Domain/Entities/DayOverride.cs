using DayGrid.Domain.Enums;

using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>Escape hatch for real life — a rest day, a template swap, or a note for a specific date.</summary>
public class DayOverride : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    /// <summary>Unique — one override per calendar date.</summary>
    public DateOnly Date { get; set; }

    public DayOverrideMode Mode { get; set; } = DayOverrideMode.UseTemplate;

    /// <summary>Set when Mode is UseTemplate — the template to use instead of the normally-resolved one.</summary>
    public Guid? TemplateId { get; set; }

    /// <summary>Shown as a banner on Home.</summary>
    public string? Note { get; set; }
}
