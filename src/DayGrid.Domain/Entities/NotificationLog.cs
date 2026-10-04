using DayGrid.Domain.Enums;

using System.Text.Json.Serialization;

namespace DayGrid.Domain.Entities;

/// <summary>What was actually delivered — powers the in-app bell and prevents duplicates.</summary>
public class NotificationLog : IUserOwned
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning account. Null only for legacy rows not yet claimed by the first registered user.</summary>
    [JsonIgnore]
    public Guid? UserId { get; set; }

    public Guid? ReminderId { get; set; }
    public Reminder? Reminder { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;

    public NotificationChannel Channel { get; set; } = NotificationChannel.InApp;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAtUtc { get; set; }

    public string? Error { get; set; }
}
