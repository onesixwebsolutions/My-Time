using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

/// <summary>
/// Deliberately has no relationships to any other entity's configuration — simple_tasks is
/// isolated by design (see plan section 4.2b and Domain/Entities/SimpleTask.cs).
/// </summary>
public class SimpleTaskConfiguration : IEntityTypeConfiguration<SimpleTask>
{
    public void Configure(EntityTypeBuilder<SimpleTask> builder)
    {
        builder.ToTable("simple_tasks");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.Priority).HasColumnName("priority").HasConversion<short>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<short>();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.Status, x.SortOrder });
    }
}
