using DayGrid.Domain.Enums;

namespace DayGrid.Domain.Entities;

/// <summary>What was actually delivered — powers the in-app bell and prevents duplicates.</summary>
public class NotificationLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid? ReminderId { get; set; }
    public Reminder? Reminder { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAtUtc { get; set; }

    public string? Error { get; set; }
}
