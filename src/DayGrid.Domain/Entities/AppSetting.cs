using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>
/// Per-user settings: exactly one row per account (unique user_id). <see cref="Id"/> is a
/// database-generated int (identity) — not a Guid entity like the rest of the domain, by design.
/// <see cref="TimeZone"/> is the user's IANA time zone and drives every "today"/"now" computation.
/// </summary>
public class AppSetting : IUserOwned
{
    public int Id { get; set; }

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public string TimeZone { get; set; } = "Asia/Kolkata";

    /// <summary>0=Sun … 6=Sat. Default Monday.</summary>
    public DayOfWeek WeekStartsOn { get; set; } = DayOfWeek.Monday;

    public TimeOnly DayStart { get; set; } = new(6, 0);
    public TimeOnly DayEnd { get; set; } = new(23, 0);

    public short DefaultSlotMinutes { get; set; } = 30;

    public bool EmailEnabled { get; set; } = true;
    public string? EmailTo { get; set; }

    /// <summary>"Here's your day" email fire time. Null disables the daily digest.</summary>
    public TimeOnly? DailyDigestTime { get; set; } = new(7, 0);

    public string Theme { get; set; } = "system";
}
