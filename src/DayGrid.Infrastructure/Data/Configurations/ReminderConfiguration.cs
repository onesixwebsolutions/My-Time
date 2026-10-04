using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminders", t => t.HasCheckConstraint(
            "ck_reminders_exactly_one_owner",
            "(future_task_id IS NOT NULL)::int + (checklist_item_id IS NOT NULL)::int = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.FutureTaskId).HasColumnName("future_task_id");
        builder.Property(x => x.ChecklistItemId).HasColumnName("checklist_item_id");
        builder.Property(x => x.OffsetMinutes).HasColumnName("offset_minutes").IsRequired();
        builder.Property(x => x.FireAtUtc).HasColumnName("fire_at_utc").IsRequired();
        builder.Property(x => x.Channels).HasColumnName("channels").HasConversion<short>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(x => x.SentAtUtc).HasColumnName("sent_at_utc");
        builder.Property(x => x.AttemptCount).HasColumnName("attempt_count").HasDefaultValue(0);

        // The dispatcher's only hot query.
        builder.HasIndex(x => new { x.Status, x.FireAtUtc });

        builder.HasOne(x => x.FutureTask)
            .WithMany(x => x.Reminders)
            .HasForeignKey(x => x.FutureTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ChecklistItem)
            .WithMany()
            .HasForeignKey(x => x.ChecklistItemId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
