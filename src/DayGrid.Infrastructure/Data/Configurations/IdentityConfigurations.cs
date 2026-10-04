using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DayGrid.Infrastructure.Data.Configurations;

// snake_case mappings for the ASP.NET Core Identity tables and the Data Protection key ring.
// They must match db/migrations/0002_auth_multitenancy.sql exactly (SchemaParityTests). These run
// after IdentityDbContext's own configuration (ApplyConfigurationsFromAssembly is called after
// base.OnModelCreating), so they override its AspNet* table names.

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("users");
        b.Property(u => u.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(u => u.UserName).HasColumnName("user_name").HasMaxLength(256);
        b.Property(u => u.NormalizedUserName).HasColumnName("normalized_user_name").HasMaxLength(256);
        b.Property(u => u.Email).HasColumnName("email").HasMaxLength(256);
        b.Property(u => u.NormalizedEmail).HasColumnName("normalized_email").HasMaxLength(256);
        b.Property(u => u.EmailConfirmed).HasColumnName("email_confirmed");
        b.Property(u => u.PasswordHash).HasColumnName("password_hash");
        b.Property(u => u.SecurityStamp).HasColumnName("security_stamp");
        b.Property(u => u.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        b.Property(u => u.PhoneNumber).HasColumnName("phone_number");
        b.Property(u => u.PhoneNumberConfirmed).HasColumnName("phone_number_confirmed");
        b.Property(u => u.TwoFactorEnabled).HasColumnName("two_factor_enabled");
        b.Property(u => u.LockoutEnd).HasColumnName("lockout_end");
        b.Property(u => u.LockoutEnabled).HasColumnName("lockout_enabled");
        b.Property(u => u.AccessFailedCount).HasColumnName("access_failed_count");
        b.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(AppUser.DisplayNameMaxLength).IsRequired();
        b.Property(u => u.CreatedAt).HasColumnName("created_at");
        b.Property(u => u.LastLoginAt).HasColumnName("last_login_at");
        b.Property(u => u.IsDisabled).HasColumnName("is_disabled"); // 0003_account_disabled.sql

        b.HasIndex(u => u.NormalizedUserName).HasDatabaseName("ux_users_normalized_user_name").IsUnique();
        // Unique (Identity's default index is not): concurrent registrations of one email can't both win.
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("ux_users_normalized_email").IsUnique();
    }
}

public class AppRoleConfiguration : IEntityTypeConfiguration<IdentityRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRole<Guid>> b)
    {
        b.ToTable("roles");
        b.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(r => r.Name).HasColumnName("name").HasMaxLength(256);
        b.Property(r => r.NormalizedName).HasColumnName("normalized_name").HasMaxLength(256);
        b.Property(r => r.ConcurrencyStamp).HasColumnName("concurrency_stamp");
        b.HasIndex(r => r.NormalizedName).HasDatabaseName("ux_roles_normalized_name").IsUnique();
    }
}

public class AppUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> b)
    {
        b.ToTable("user_roles");
        b.Property(ur => ur.UserId).HasColumnName("user_id");
        b.Property(ur => ur.RoleId).HasColumnName("role_id");
    }
}

public class AppUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b)
    {
        b.ToTable("user_claims");
        b.Property(c => c.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        b.Property(c => c.UserId).HasColumnName("user_id");
        b.Property(c => c.ClaimType).HasColumnName("claim_type");
        b.Property(c => c.ClaimValue).HasColumnName("claim_value");
    }
}

public class AppUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b)
    {
        b.ToTable("user_logins");
        b.Property(l => l.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        b.Property(l => l.ProviderKey).HasColumnName("provider_key").HasMaxLength(128);
        b.Property(l => l.ProviderDisplayName).HasColumnName("provider_display_name");
        b.Property(l => l.UserId).HasColumnName("user_id");
    }
}

public class AppUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b)
    {
        b.ToTable("user_tokens");
        b.Property(t => t.UserId).HasColumnName("user_id");
        b.Property(t => t.LoginProvider).HasColumnName("login_provider").HasMaxLength(128);
        b.Property(t => t.Name).HasColumnName("name").HasMaxLength(128);
        b.Property(t => t.Value).HasColumnName("value");
    }
}

public class AppRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> b)
    {
        b.ToTable("role_claims");
        b.Property(c => c.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        b.Property(c => c.RoleId).HasColumnName("role_id");
        b.Property(c => c.ClaimType).HasColumnName("claim_type");
        b.Property(c => c.ClaimValue).HasColumnName("claim_value");
    }
}

public class DataProtectionKeyConfiguration : IEntityTypeConfiguration<DataProtectionKey>
{
    public void Configure(EntityTypeBuilder<DataProtectionKey> b)
    {
        b.ToTable("data_protection_keys");
        b.HasKey(k => k.Id);
        b.Property(k => k.Id).HasColumnName("id").UseIdentityByDefaultColumn();
        b.Property(k => k.FriendlyName).HasColumnName("friendly_name");
        b.Property(k => k.Xml).HasColumnName("xml");
    }
}
