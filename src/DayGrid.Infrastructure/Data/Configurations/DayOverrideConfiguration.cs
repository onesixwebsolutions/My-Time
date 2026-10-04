using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class DayOverrideConfiguration : IEntityTypeConfiguration<DayOverride>
{
    public void Configure(EntityTypeBuilder<DayOverride> builder)
    {
        builder.ToTable("day_overrides");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Date).HasColumnName("date").IsRequired();
        builder.Property(x => x.Mode).HasColumnName("mode").HasConversion<short>();
        builder.Property(x => x.TemplateId).HasColumnName("template_id");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(200);

        builder.HasIndex(x => x.Date).IsUnique();

        builder.HasOne<TimetableTemplate>()
            .WithMany()
            .HasForeignKey(x => x.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
