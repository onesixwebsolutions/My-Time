using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("app_settings", t =>
            t.HasCheckConstraint("ck_app_settings_singleton", "id = 1"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();

        builder.Property(x => x.TimeZone).HasColumnName("time_zone").HasMaxLength(60).IsRequired();
        builder.Property(x => x.WeekStartsOn).HasColumnName("week_starts_on").HasConversion<short>();
        builder.Property(x => x.DayStart).HasColumnName("day_start");
        builder.Property(x => x.DayEnd).HasColumnName("day_end");
        builder.Property(x => x.DefaultSlotMinutes).HasColumnName("default_slot_minutes");
        builder.Property(x => x.EmailEnabled).HasColumnName("email_enabled");
        builder.Property(x => x.EmailTo).HasColumnName("email_to").HasMaxLength(200);
        builder.Property(x => x.DailyDigestTime).HasColumnName("daily_digest_time");
        builder.Property(x => x.Theme).HasColumnName("theme").HasMaxLength(20);

        // Seed the single settings row so the app has sane defaults from a fresh migration,
        // matching plan section 4.2.
        builder.HasData(new AppSetting
        {
            Id = 1,
            TimeZone = "Asia/Kolkata",
            WeekStartsOn = DayOfWeek.Monday,
            DayStart = new TimeOnly(6, 0),
            DayEnd = new TimeOnly(23, 0),
            DefaultSlotMinutes = 30,
            EmailEnabled = true,
            EmailTo = "onesixwebsolutions@gmail.com",
            DailyDigestTime = new TimeOnly(7, 0),
            Theme = "system"
        });
    }
}
