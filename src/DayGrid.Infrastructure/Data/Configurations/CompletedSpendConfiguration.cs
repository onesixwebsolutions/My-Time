using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class CompletedSpendConfiguration : IEntityTypeConfiguration<CompletedSpend>
{
    public void Configure(EntityTypeBuilder<CompletedSpend> builder)
    {
        builder.ToTable("completed_spends");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Title).HasColumnName("title").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(60);
        builder.Property(x => x.Date).HasColumnName("spend_date");
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.Date);
    }
}
