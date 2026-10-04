using Microsoft.AspNetCore.Identity;

namespace DayGrid.Api.Auth;

/// <summary>How the site's first administrator comes into being.</summary>
public enum BootstrapAdminMode
{
    /// <summary>No automatic Admin (Production without <c>Auth:BootstrapAdminEmail</c>).</summary>
    None,

    /// <summary>The account whose (normalized) email equals <c>Auth:BootstrapAdminEmail</c>
    /// becomes Admin when it confirms its email address.</summary>
    ConfiguredEmail,

    /// <summary>The first account to confirm its email address becomes Admin (only while no Admin
    /// exists). Development and the single-user desktop exe (Embedded mode) only.</summary>
    FirstConfirmedUser
}

/// <summary>
/// Decides which account is granted Admin — and claims the legacy (user_id NULL) rows — at email
/// confirmation time. Never at registration: an unconfirmed registration proves nothing, so the
/// old "first registrant becomes Admin" rule let anyone who registered first take the site over.
/// </summary>
public sealed class BootstrapAdminPolicy
{
    public const string EmailConfigKey = "Auth:BootstrapAdminEmail";

    private BootstrapAdminPolicy(BootstrapAdminMode mode, string? normalizedEmail)
    {
        Mode = mode;
        NormalizedEmail = normalizedEmail;
    }

    public BootstrapAdminMode Mode { get; }

    /// <summary>Normalized bootstrap email (ConfiguredEmail mode only).</summary>
    public string? NormalizedEmail { get; }

    public static BootstrapAdminPolicy None() => new(BootstrapAdminMode.None, null);

    public static BootstrapAdminPolicy FirstConfirmedUser() => new(BootstrapAdminMode.FirstConfirmedUser, null);

    public static BootstrapAdminPolicy ForEmail(string email, ILookupNormalizer normalizer) =>
        new(BootstrapAdminMode.ConfiguredEmail, normalizer.NormalizeEmail(email.Trim()));

    /// <summary>
    /// <c>Auth:BootstrapAdminEmail</c> when configured; otherwise "first user to confirm" in
    /// Development or Embedded mode (a personal install), and no automatic Admin anywhere else.
    /// </summary>
    public static BootstrapAdminPolicy FromConfiguration(IConfiguration configuration, IHostEnvironment environment, ILookupNormalizer normalizer)
    {
        var email = configuration[EmailConfigKey];
        if (!string.IsNullOrWhiteSpace(email))
            return ForEmail(email, normalizer);

        var embedded = string.Equals(configuration["Database:Mode"], "Embedded", StringComparison.OrdinalIgnoreCase);
        return environment.IsDevelopment() || embedded ? FirstConfirmedUser() : None();
    }

    /// <summary>Whether <paramref name="normalizedEmail"/> may become Admin by this policy
    /// (FirstConfirmedUser additionally requires that no Admin exists yet — checked by the caller).</summary>
    public bool Matches(string? normalizedEmail) => Mode switch
    {
        BootstrapAdminMode.ConfiguredEmail => normalizedEmail is not null && string.Equals(normalizedEmail, NormalizedEmail, StringComparison.Ordinal),
        BootstrapAdminMode.FirstConfirmedUser => true,
        _ => false
    };
}
