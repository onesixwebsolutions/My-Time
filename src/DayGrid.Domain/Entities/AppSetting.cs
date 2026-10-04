namespace DayGrid.Domain.Entities;

/// <summary>
/// Single-row settings table. <see cref="Id"/> is fixed to 1 (int PK, CHECK id = 1) — this is
/// not a Guid entity like the rest of the domain, by design.
/// </summary>
public class AppSetting
{
    public int Id { get; set; } = 1;

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
