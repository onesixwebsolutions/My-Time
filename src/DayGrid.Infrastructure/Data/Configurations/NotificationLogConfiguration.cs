using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_log");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ReminderId).HasColumnName("reminder_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasColumnType("text").IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<short>();
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc");
        builder.Property(x => x.ReadAtUtc).HasColumnName("read_at_utc");
        builder.Property(x => x.Error).HasColumnName("error").HasColumnType("text");

        builder.HasIndex(x => x.CreatedAtUtc);
        builder.HasIndex(x => x.ReadAtUtc);

        builder.HasOne(x => x.Reminder)
            .WithMany()
            .HasForeignKey(x => x.ReminderId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
