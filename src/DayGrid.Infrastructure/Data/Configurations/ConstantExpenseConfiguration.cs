using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class ConstantExpenseConfiguration : IEntityTypeConfiguration<ConstantExpense>
{
    public void Configure(EntityTypeBuilder<ConstantExpense> builder)
    {
        builder.ToTable("constant_expenses");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");

        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.Amount).HasColumnName("amount").HasColumnType("numeric(12,2)");
        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(60);
        builder.Property(x => x.DayOfMonth).HasColumnName("day_of_month");
        builder.Property(x => x.Notes).HasColumnName("notes").HasColumnType("text");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.IsActive);
    }
}
