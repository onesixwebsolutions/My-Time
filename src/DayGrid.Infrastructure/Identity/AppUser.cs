using Microsoft.AspNetCore.Identity;

namespace DayGrid.Infrastructure.Identity;

/// <summary>An account. The email address is also the user name (see AuthEndpoints).</summary>
public class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Disabled by an administrator (admin "lock"). Distinct from Identity's brute-force lockout
    /// (LockoutEnd): a password reset ends a lockout but never re-enables a disabled account; only
    /// an admin "unlock" does. Disabled accounts cannot sign in, reset their password or confirm.
    /// </summary>
    public bool IsDisabled { get; set; }
}
