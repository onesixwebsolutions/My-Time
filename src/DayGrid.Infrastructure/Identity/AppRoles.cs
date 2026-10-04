namespace DayGrid.Infrastructure.Identity;

/// <summary>The two roles, seeded with fixed ids by db/migrations/0002_auth_multitenancy.sql.</summary>
public static class AppRoles
{
    public const string User = "User";
    public const string Admin = "Admin";

    public static readonly Guid UserRoleId = Guid.Parse("3f0c8f1e-7a52-4d0e-9a51-6b1f2d3c4e01");
    public static readonly Guid AdminRoleId = Guid.Parse("3f0c8f1e-7a52-4d0e-9a51-6b1f2d3c4e02");

    /// <summary>Claim added to every principal issued for a user whose email is confirmed; the
    /// fallback authorization policy requires it.</summary>
    public const string EmailConfirmedClaim = "daygrid:email_confirmed";
}
