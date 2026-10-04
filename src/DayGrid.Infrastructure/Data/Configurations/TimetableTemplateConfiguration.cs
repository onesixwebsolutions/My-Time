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
        builder.Property(x => x.DayStart).HasColumnName("day_start").HasDefaultValue(new TimeOnly(6, 0));
        builder.Property(x => x.DayEnd).HasColumnName("day_end").HasDefaultValue(new TimeOnly(23, 0));
        builder.Property(x => x.SlotMinutes).HasColumnName("slot_minutes").HasDefaultValue((short)30);
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
