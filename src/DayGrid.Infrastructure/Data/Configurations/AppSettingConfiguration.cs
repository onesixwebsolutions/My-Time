using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").UseIdentityByDefaultColumn();

        builder.Property(x => x.TimeZone).HasColumnName("time_zone").HasMaxLength(60).IsRequired();
        builder.Property(x => x.WeekStartsOn).HasColumnName("week_starts_on").HasConversion<short>();
        builder.Property(x => x.DayStart).HasColumnName("day_start");
        builder.Property(x => x.DayEnd).HasColumnName("day_end");
        builder.Property(x => x.DefaultSlotMinutes).HasColumnName("default_slot_minutes");
        builder.Property(x => x.EmailEnabled).HasColumnName("email_enabled");
        builder.Property(x => x.EmailTo).HasColumnName("email_to").HasMaxLength(200);
        builder.Property(x => x.DailyDigestTime).HasColumnName("daily_digest_time");
        builder.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(20);

        // One settings row per user (migration 0002). Rows are created at registration
        // (AccountService) — there is no global seed row any more.
        builder.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("ux_app_settings_user_id");
    }
}
