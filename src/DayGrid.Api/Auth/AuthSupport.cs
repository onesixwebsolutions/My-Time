using System.Security.Claims;
using System.Text;
using DayGrid.Application.Time;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Auth;

/// <summary>UserDto from the auth contract.</summary>
public sealed record UserDto(
    Guid Id, string Email, string DisplayName, string TimeZone, IReadOnlyList<string> Roles, bool EmailConfirmed, DateTimeOffset CreatedAt);

/// <summary>AdminUserDto from the auth contract. <c>LockedOut</c>: disabled by an admin or in a
/// brute-force lockout; <c>Disabled</c>: disabled by an admin (survives a password reset).</summary>
public sealed record AdminUserDto(
    Guid Id, string Email, string DisplayName, IReadOnlyList<string> Roles, bool EmailConfirmed, bool LockedOut, bool Disabled,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt);

/// <summary>Shared helpers for the auth/admin endpoints: problem responses, the XSRF cookie,
/// email-link building and token encoding.</summary>
public static class AuthSupport
{
    public const string XsrfCookieName = "XSRF-TOKEN";
    public const string XsrfHeaderName = "X-XSRF-TOKEN";
    public const string AuthCookieName = "daygrid.auth";
    public const string AntiforgeryCookieName = "daygrid.af";
    public const string AuthRateLimitPolicy = "auth";
    public const string AdminPolicy = "Admin";

    /// <summary>RFC 7807 problem with the contract's <c>code</c> extension.</summary>
    public static IResult Problem(int status, string code, string title, string? detail = null) =>
        Results.Problem(statusCode: status, title: title, detail: detail,
            extensions: new Dictionary<string, object?> { ["code"] = code });

    public static IResult Validation(IDictionary<string, string[]> errors) =>
        Results.ValidationProblem(errors, title: "One or more validation errors occurred.",
            extensions: new Dictionary<string, object?> { ["code"] = "validation" });

    public static IResult Validation(string field, string message) =>
        Validation(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult Unauthenticated() =>
        Problem(StatusCodes.Status401Unauthorized, "unauthenticated", "Authentication is required.");

    /// <summary>
    /// Writes the readable <c>XSRF-TOKEN</c> cookie (the antiforgery request token for the
    /// principal currently on <paramref name="context"/>). Callers that just signed in or out
    /// must set <c>HttpContext.User</c> to the new principal first — the token is bound to it.
    /// </summary>
    public static void IssueXsrfCookie(HttpContext context)
    {
        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var tokens = antiforgery.GetAndStoreTokens(context);
        var env = context.RequestServices.GetRequiredService<IHostEnvironment>();
        context.Response.Cookies.Append(XsrfCookieName, tokens.RequestToken!, new CookieOptions
        {
            HttpOnly = false, // Angular reads it and echoes it in X-XSRF-TOKEN
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Secure = !env.IsDevelopment() || context.Request.IsHttps,
            IsEssential = true
        });
    }

    /// <summary>Identity tokens are not URL-safe; links carry them Base64Url-encoded.</summary>
    public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static string? DecodeToken(string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        try
        {
            return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded.Trim()));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Base URL for links in emails: <c>App:PublicBaseUrl</c> (mandatory in Production, checked at
    /// startup), otherwise the request's own origin.
    /// </summary>
    public static string PublicBaseUrl(HttpContext context)
    {
        var configured = context.RequestServices.GetRequiredService<IConfiguration>()["App:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured))
            return configured.Trim().TrimEnd('/');
        return $"{context.Request.Scheme}://{context.Request.Host}".TrimEnd('/');
    }

    public static async Task<UserDto> ToUserDtoAsync(AppUser user, UserManager<AppUser> userManager, AppDbContext db, IAppClockFactory clocks, CancellationToken ct)
    {
        var roles = (await userManager.GetRolesAsync(user)).OrderBy(r => r, StringComparer.Ordinal).ToList();
        var timeZone = await db.AppSettings.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .Select(s => s.TimeZone)
            .FirstOrDefaultAsync(ct);
        return new UserDto(user.Id, user.Email ?? string.Empty, user.DisplayName,
            string.IsNullOrWhiteSpace(timeZone) ? clocks.DefaultTimeZoneId : timeZone,
            roles, user.EmailConfirmed, user.CreatedAt);
    }

    public static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    public static Guid? UserId(ClaimsPrincipal principal) =>
        DayGrid.Infrastructure.Security.CurrentUserContext.FromPrincipal(principal);
}

/// <summary>Structured audit trail (logger category <c>DayGrid.Audit</c>). Never logs passwords or tokens.</summary>
public sealed class AuditLog
{
    private readonly ILogger _logger;

    public AuditLog(ILoggerFactory loggerFactory) => _logger = loggerFactory.CreateLogger("DayGrid.Audit");

    public void Write(string auditEvent, HttpContext context, Guid? userId, string? email = null, string? detail = null) =>
        _logger.LogInformation(
            "Audit {AuditEvent}: user {UserId} email {Email} ip {ClientIp} {Detail}",
            auditEvent, userId, email, AuthSupport.ClientIp(context), detail ?? string.Empty);

    public void Warn(string auditEvent, HttpContext context, Guid? userId, string? email = null, string? detail = null) =>
        _logger.LogWarning(
            "Audit {AuditEvent}: user {UserId} email {Email} ip {ClientIp} {Detail}",
            auditEvent, userId, email, AuthSupport.ClientIp(context), detail ?? string.Empty);
}
