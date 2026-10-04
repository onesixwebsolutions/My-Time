using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class TimetableBlockConfiguration : IEntityTypeConfiguration<TimetableBlock>
{
    public void Configure(EntityTypeBuilder<TimetableBlock> builder)
    {
        builder.ToTable("timetable_blocks", t =>
            t.HasCheckConstraint("ck_timetable_blocks_end_after_start", "end_time > start_time"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TemplateId).HasColumnName("template_id");
        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.StartTime).HasColumnName("start_time").IsRequired();
        builder.Property(x => x.EndTime).HasColumnName("end_time").IsRequired();
        builder.Property(x => x.Category).HasColumnName("category").HasConversion<short>();
        builder.Property(x => x.Color).HasColumnName("color").HasMaxLength(9);
        builder.Property(x => x.Location).HasColumnName("location").HasMaxLength(120);
        builder.Property(x => x.ChecklistId).HasColumnName("checklist_id");
        builder.Property(x => x.AllowOverlap).HasColumnName("allow_overlap").HasDefaultValue(false);
        builder.Property(x => x.NotifyAtStart).HasColumnName("notify_at_start").HasDefaultValue(false);
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");

        builder.HasIndex(x => new { x.TemplateId, x.StartTime });

        // checklist_id is a soft reference: surfacing a checklist inside a block should not
        // fail (or cascade-delete the block) if that checklist is later removed.
        builder.HasOne<Checklist>()
            .WithMany()
            .HasForeignKey(x => x.ChecklistId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
