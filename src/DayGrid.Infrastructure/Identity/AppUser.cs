using Microsoft.AspNetCore.Identity;

namespace DayGrid.Infrastructure.Identity;

/// <summary>An account. The email address is also the user name (see AuthEndpoints).</summary>
public class AppUser : IdentityUser<Guid>
{
    public const int DisplayNameMaxLength = 100;

    public string DisplayName { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }
}
