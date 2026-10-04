using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class FutureTaskConfiguration : IEntityTypeConfiguration<FutureTask>
{
    public void Configure(EntityTypeBuilder<FutureTask> builder)
    {
        builder.ToTable("future_tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.DueDate).HasColumnName("due_date").IsRequired();
        builder.Property(x => x.DueTime).HasColumnName("due_time");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(60);
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<short>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.PromoteToChecklistId).HasColumnName("promote_to_checklist_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.Status, x.DueDate });

        builder.HasOne<Checklist>()
            .WithMany()
            .HasForeignKey(x => x.PromoteToChecklistId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Reminders)
            .WithOne(x => x.FutureTask)
            .HasForeignKey(x => x.FutureTaskId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
