using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class TimetableTemplateConfiguration : IEntityTypeConfiguration<TimetableTemplate>
{
    public void Configure(EntityTypeBuilder<TimetableTemplate> builder)
    {
        builder.ToTable("timetable_templates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasColumnType("text");
        builder.Property(x => x.IsDefault).HasColumnName("is_default");
        // No HasDefaultValue here: EF treats the CLR default (00:00 / 0) as "not set" and omits it
        // from the INSERT, so the database default replaced it — a template starting at midnight
        // was saved as 06:00. The entity already initialises 06:00/23:00/30; init.sql keeps the
        // column defaults for raw SQL inserts.
        builder.Property(x => x.DayStart).HasColumnName("day_start");
        builder.Property(x => x.DayEnd).HasColumnName("day_end");
        builder.Property(x => x.SlotMinutes).HasColumnName("slot_minutes");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(x => x.Blocks)
            .WithOne(x => x.Template)
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Assignments)
            .WithOne(x => x.Template)
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
