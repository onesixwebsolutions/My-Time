using System.Text.Json;
using DayGrid.Domain.Entities;
using DayGrid.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_items");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ChecklistId).HasColumnName("checklist_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<short>();
        builder.Property(x => x.EstimatedMinutes).HasColumnName("estimated_minutes");
        builder.Property(x => x.AnchorType).HasColumnName("anchor_type").HasConversion<short>();
        builder.Property(x => x.AnchorTime).HasColumnName("anchor_time");
        builder.Property(x => x.WindowStart).HasColumnName("window_start");
        builder.Property(x => x.WindowEnd).HasColumnName("window_end");
        builder.Property(x => x.TimetableBlockId).HasColumnName("timetable_block_id");

        var recurrenceComparer = new ValueComparer<RecurrenceRule>(
            (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
            v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
            v => JsonSerializer.Deserialize<RecurrenceRule>(JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!);

        builder.Property(x => x.Recurrence)
            .HasColumnName("recurrence")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => JsonSerializer.Deserialize<RecurrenceRule>(v, JsonOptions) ?? new RecurrenceRule())
            .Metadata.SetValueComparer(recurrenceComparer);

        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.ReminderOffsetMinutes).HasColumnName("reminder_offset_minutes");
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.ChecklistId, x.IsActive });
        builder.HasIndex(x => new { x.AnchorType, x.AnchorTime });
        builder.HasIndex(x => x.Recurrence).HasMethod("gin");

        builder.HasMany(x => x.Completions)
            .WithOne(x => x.ChecklistItem)
            .HasForeignKey(x => x.ChecklistItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
