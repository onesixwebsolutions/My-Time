using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Azure.Identity;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace DayGrid.Api.Auth;

/// <summary>Service registration for Identity, the auth cookie, antiforgery, authorization
/// policies, rate limiting and Data Protection. Everything config/environment dependent is bound
/// lazily (options), so test hosts can override it.</summary>
public static class AuthSetup
{
    public const string EmailConfirmationTokenProvider = "EmailConfirmation";

    public static IServiceCollection AddDayGridAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
            {
                // NIST 800-63B: length over composition rules.
                options.Password.RequiredLength = 10;
                options.Password.RequiredUniqueChars = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;

                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;

                options.User.RequireUniqueEmail = true;
                options.User.AllowedUserNameCharacters = string.Empty; // the user name is the email address

                options.SignIn.RequireConfirmedEmail = true;
                options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider;

                // Map claims to the conventional types (NameIdentifier = user id).
                options.ClaimsIdentity.UserIdClaimType = ClaimTypes.NameIdentifier;
            })
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<EmailConfirmationTokenProvider<AppUser>>(EmailConfirmationTokenProvider)
            .AddPasswordValidator<MaxLengthPasswordValidator>()
            .AddClaimsPrincipalFactory<AppClaimsPrincipalFactory>()
            .AddSignInManager<AppSignInManager>();

        // Password-reset tokens (default provider) are short-lived; confirmation links last longer.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(2));
        services.Configure<EmailConfirmationTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromDays(3));

        services.AddOptions<SecurityStampValidatorOptions>().Configure<IConfiguration>((o, config) =>
            o.ValidationInterval = TimeSpan.FromSeconds(Math.Max(0, config.GetValue("Auth:SecurityStampValidationIntervalSeconds", 60))));

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IHostEnvironment>((o, env) =>
            {
                o.Cookie.Name = AuthSupport.AuthCookieName;
                o.Cookie.HttpOnly = true;
                o.Cookie.SameSite = SameSiteMode.Strict;
                o.Cookie.SecurePolicy = env.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
                o.Cookie.IsEssential = true;
                o.ExpireTimeSpan = TimeSpan.FromDays(14);
                o.SlidingExpiration = true;
                // An API never redirects to a login page.
                o.Events.OnRedirectToLogin = context =>
                {
                    // The caller is anonymous now (expired/revoked session): hand out an XSRF token
                    // for an anonymous caller so the SPA's next POST (login) is accepted.
                    AuthSupport.IssueXsrfCookie(context.HttpContext);
                    return WriteProblemAsync(context.HttpContext, StatusCodes.Status401Unauthorized, "unauthenticated", "Authentication is required.");
                };
                o.Events.OnRedirectToAccessDenied = context => WriteProblemAsync(context.HttpContext, StatusCodes.Status403Forbidden, "forbidden", "You do not have access to this resource.");
                o.Events.OnRedirectToLogout = context => { context.Response.StatusCode = StatusCodes.Status204NoContent; return Task.CompletedTask; };
                o.Events.OnRedirectToReturnUrl = context => { context.Response.StatusCode = StatusCodes.Status204NoContent; return Task.CompletedTask; };
            });

        services.AddAntiforgery(o =>
        {
            o.HeaderName = AuthSupport.XsrfHeaderName;
            o.Cookie.Name = AuthSupport.AntiforgeryCookieName;
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.Path = "/";
            o.SuppressXFrameOptionsHeader = true; // SecurityHeadersMiddleware sends DENY itself
            // SameAsRequest, not Always: with Always the antiforgery system throws on every plain-HTTP
            // request (the desktop exe serves http://localhost). Behind HTTPS (HSTS + redirection +
            // forwarded headers on Azure) the cookie is Secure anyway. It is not a credential by itself.
            o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

        var confirmed = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(AppRoles.EmailConfirmedClaim, "true")
            .Build();
        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(confirmed)
            .SetFallbackPolicy(confirmed)
            .AddPolicy(AuthSupport.AdminPolicy, p => p.Combine(confirmed).RequireRole(AppRoles.Admin));

        services.AddRateLimiter(_ => { });
        services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((o, config) =>
        {
            var permitLimit = Math.Max(1, config.GetValue("RateLimiting:Auth:PermitLimit", 10));
            var window = TimeSpan.FromSeconds(Math.Max(1, config.GetValue("RateLimiting:Auth:WindowSeconds", 60)));
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(AuthSupport.AuthRateLimitPolicy, context => RateLimitPartition.GetFixedWindowLimiter(
                AuthSupport.ClientIp(context),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = permitLimit,
                    Window = window,
                    QueueLimit = 0,
                    AutoReplenishment = true
                }));
            o.OnRejected = async (context, ct) =>
            {
                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var value) ? value : window;
                context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await WriteProblemAsync(context.HttpContext, StatusCodes.Status429TooManyRequests, "rate_limited", "Too many requests. Try again later.");
            };
        });

        // Keys in the database: cookies and tokens survive restarts and work across instances.
        // DataProtection:KeyVaultKeyUri (optional) additionally encrypts the key ring at rest with
        // an Azure Key Vault key, authenticating with DefaultAzureCredential (managed identity on
        // App Service). Unset: keys are stored unencrypted in the database (warning in Production).
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("DayGrid")
            .PersistKeysToDbContext<AppDbContext>();
        if (KeyVaultKeyUri(configuration) is { } keyUri)
            dataProtection.ProtectKeysWithAzureKeyVault(keyUri, new DefaultAzureCredential());

        services.AddSingleton(sp => BootstrapAdminPolicy.FromConfiguration(
            sp.GetRequiredService<IConfiguration>(), sp.GetRequiredService<IHostEnvironment>(), sp.GetRequiredService<ILookupNormalizer>()));
        services.AddSingleton<UnknownAccountLockout>();
        services.AddScoped<AccountService>();
        services.AddSingleton<AuditLog>();
        return services;
    }

    public const string KeyVaultKeyUriConfigKey = "DataProtection:KeyVaultKeyUri";

    /// <summary>The configured Key Vault key URI, or null when unset. A value that is not an
    /// absolute https URI fails startup (a typo must not silently disable key encryption).</summary>
    public static Uri? KeyVaultKeyUri(IConfiguration configuration)
    {
        var value = configuration[KeyVaultKeyUriConfigKey];
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException(
                $"{KeyVaultKeyUriConfigKey} must be an absolute https URI of a Key Vault key (e.g. https://myvault.vault.azure.net/keys/dataprotection).");
        return uri;
    }

    internal static async Task WriteProblemAsync(HttpContext context, int status, string code, string title)
    {
        if (context.Response.HasStarted)
            return;
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { type = "about:blank", title, status, code }));
    }
}

/// <summary>Adds the email-confirmed claim the fallback authorization policy requires.</summary>
public sealed class AppClaimsPrincipalFactory : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>
{
    public AppClaimsPrincipalFactory(UserManager<AppUser> userManager, RoleManager<IdentityRole<Guid>> roleManager, IOptions<IdentityOptions> options)
        : base(userManager, roleManager, options)
    {
    }

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.EmailConfirmed)
            identity.AddClaim(new Claim(AppRoles.EmailConfirmedClaim, "true"));
        return identity;
    }
}

/// <summary>Passwords are capped at 128 characters (contract), bounding hashing cost.</summary>
public sealed class MaxLengthPasswordValidator : IPasswordValidator<AppUser>
{
    public Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user, string? password) =>
        Task.FromResult(password is { Length: > AuthEndpoints.MaxPasswordLength }
            ? IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Passwords must be at most {AuthEndpoints.MaxPasswordLength} characters."
            })
            : IdentityResult.Success);
}

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public EmailConfirmationTokenProviderOptions()
    {
        Name = AuthSetup.EmailConfirmationTokenProvider;
        TokenLifespan = TimeSpan.FromDays(3);
    }
}

public sealed class EmailConfirmationTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
{
    public EmailConfirmationTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<EmailConfirmationTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger)
        : base(dataProtectionProvider, options, logger)
    {
    }
}
