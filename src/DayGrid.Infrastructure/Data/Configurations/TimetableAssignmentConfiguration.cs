using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class TimetableAssignmentConfiguration : IEntityTypeConfiguration<TimetableAssignment>
{
    public void Configure(EntityTypeBuilder<TimetableAssignment> builder)
    {
        builder.ToTable("timetable_assignments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.TemplateId).HasColumnName("template_id");
        builder.Property(x => x.Scope).HasColumnName("scope").HasConversion<short>();
        builder.Property(x => x.DayOfWeek).HasColumnName("day_of_week").HasConversion<short?>();
        builder.Property(x => x.DateFrom).HasColumnName("date_from");
        builder.Property(x => x.DateTo).HasColumnName("date_to");
        builder.Property(x => x.Priority).HasColumnName("priority").HasDefaultValue(0);

        builder.HasIndex(x => new { x.Scope, x.DayOfWeek });
        builder.HasIndex(x => new { x.Scope, x.DateFrom, x.DateTo });
    }
}
