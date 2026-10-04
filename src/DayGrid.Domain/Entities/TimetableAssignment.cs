using DayGrid.Domain.Enums;

using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// Which template applies when. Resolution for a given date is most-specific-wins:
/// SpecificDate → DateRange → Weekday → template with IsDefault.
/// </summary>
public class TimetableAssignment : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public Guid TemplateId { get; set; }
    public TimetableTemplate? Template { get; set; }

    public AssignmentScope Scope { get; set; }

    /// <summary>Set when Scope is Weekday. 0=Sun … 6=Sat.</summary>
    public DayOfWeek? DayOfWeek { get; set; }

    /// <summary>Set when Scope is SpecificDate (DateFrom only) or DateRange (both).</summary>
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }

    /// <summary>Manual tie-break among assignments of the same scope.</summary>
    public int Priority { get; set; }
}
