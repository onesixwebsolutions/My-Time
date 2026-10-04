using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class ChecklistCompletionConfiguration : IEntityTypeConfiguration<ChecklistCompletion>
{
    public void Configure(EntityTypeBuilder<ChecklistCompletion> builder)
    {
        builder.ToTable("checklist_completions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.ChecklistItemId).HasColumnName("checklist_item_id");
        builder.Property(x => x.OccurrenceDate).HasColumnName("occurrence_date").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(x => x.Note).HasColumnName("note").HasColumnType("text");

        // Makes toggling idempotent and gives streak stats for free.
        builder.HasIndex(x => new { x.ChecklistItemId, x.OccurrenceDate }).IsUnique();
    }
}
