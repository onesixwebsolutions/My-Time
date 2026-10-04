using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Security.Claims;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Auth;

public record RegisterRequest(string? Email, string? Password, string? DisplayName);
public record ConfirmEmailRequest(string? UserId, string? Token, string? Password);
public record EmailRequest(string? Email);
public record LoginRequest(string? Email, string? Password, bool RememberMe);
public record ResetPasswordRequest(string? Email, string? Token, string? NewPassword);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);
public record UpdateProfileRequest(string? DisplayName, string? TimeZone);
public record DeleteAccountRequest(string? Password);

/// <summary>
/// /api/v1/auth — registration with mandatory email confirmation, cookie sign-in, password
/// reset/change, profile and account deletion. See the auth contract for shapes and codes.
/// Every unsafe request (including login/register) must carry X-XSRF-TOKEN (validated by the
/// antiforgery middleware in Program.cs).
///
/// Account enumeration: register, resend-confirmation, forgot-password and login answer with the
/// same status and body whether or not an account exists; emails go out through a background
/// queue (<see cref="IAccountEmailQueue"/>) so sending one never changes the response time.
/// </summary>
public static class AuthEndpoints
{
    public const int MaxEmailLength = 256;
    public const int MaxPasswordLength = 128;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth").WithTags("Auth");

        group.MapGet("/me", GetMe).AllowAnonymous().WithName("GetCurrentUser");
        group.MapPut("/me", UpdateMe).WithName("UpdateCurrentUser");
        group.MapDelete("/me", DeleteMe).RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("DeleteCurrentUser");

        group.MapPost("/register", Register).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("Register");
        group.MapPost("/confirm-email", ConfirmEmail).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("ConfirmEmail");
        group.MapPost("/resend-confirmation", ResendConfirmation).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("ResendConfirmation");
        group.MapPost("/login", Login).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("Login");
        group.MapPost("/logout", Logout).WithName("Logout");
        group.MapPost("/forgot-password", ForgotPassword).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("ForgotPassword");
        group.MapPost("/reset-password", ResetPassword).AllowAnonymous().RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("ResetPassword");
        group.MapPost("/change-password", ChangePassword).RequireRateLimiting(AuthSupport.AuthRateLimitPolicy).WithName("ChangePassword");

        return app;
    }

    // ------------------------------------------------------------------ me

    private static async Task<IResult> GetMe(HttpContext context, UserManager<AppUser> userManager, AppDbContext db, IAppClockFactory clocks, CancellationToken ct)
    {
        var user = context.User.Identity?.IsAuthenticated == true ? await userManager.GetUserAsync(context.User) : null;
        if (user is null && context.User.Identity?.IsAuthenticated == true)
            context.User = new ClaimsPrincipal(new ClaimsIdentity()); // stale cookie: token for an anonymous caller
        AuthSupport.IssueXsrfCookie(context);
        if (user is null)
            return AuthSupport.Unauthenticated();
        return Results.Ok(await AuthSupport.ToUserDtoAsync(user, userManager, db, clocks, ct));
    }

    private static async Task<IResult> UpdateMe(
        HttpContext context, UpdateProfileRequest? request, UserManager<AppUser> userManager, AppDbContext db, IAppClockFactory clocks, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return AuthSupport.Unauthenticated();

        var errors = new Dictionary<string, string[]>();
        var displayName = request?.DisplayName?.Trim();
        if (ValidateDisplayName(displayName) is { } nameError)
            errors["displayName"] = [nameError];
        var timeZone = request?.TimeZone?.Trim();
        if (!clocks.IsValidTimeZone(timeZone))
            errors["timeZone"] = ["Time zone must be a valid IANA time zone id, e.g. 'Asia/Kolkata'."];
        if (errors.Count > 0)
            return AuthSupport.Validation(errors);

        user.DisplayName = displayName!;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return AuthSupport.Validation(MapIdentityErrors(result));

        // The time zone lives in the user's settings row (single source for every clock).
        var settings = await db.AppSettings.FirstOrDefaultAsync(s => s.UserId == user.Id, ct);
        if (settings is null)
        {
            settings = new AppSetting { UserId = user.Id, EmailTo = user.Email };
            db.AppSettings.Add(settings);
        }
        settings.TimeZone = timeZone!;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await AuthSupport.ToUserDtoAsync(user, userManager, db, clocks, ct));
    }

    private static async Task<IResult> DeleteMe(
        HttpContext context, [FromBody] DeleteAccountRequest? request, UserManager<AppUser> userManager,
        SignInManager<AppUser> signInManager, AccountService accounts, AuditLog audit, CancellationToken ct)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return AuthSupport.Unauthenticated();

        if (string.IsNullOrEmpty(request?.Password) || !await userManager.CheckPasswordAsync(user, request.Password))
        {
            audit.Warn("account.delete.failed", context, user.Id, user.Email, "wrong password");
            return WrongPassword();
        }

        // Never leave the site without an administrator: the only Admin must hand the role on first.
        if (await userManager.IsInRoleAsync(user, AppRoles.Admin)
            && (await userManager.GetUsersInRoleAsync(AppRoles.Admin)).Count <= 1)
        {
            audit.Warn("account.delete.refused", context, user.Id, user.Email, "last admin");
            return AuthSupport.Problem(StatusCodes.Status400BadRequest, "last_admin",
                "You are the only administrator. Make another account an administrator before deleting yours.");
        }

        var result = await accounts.DeleteAsync(user, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException("Account deletion failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));

        await signInManager.SignOutAsync();
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        AuthSupport.IssueXsrfCookie(context);
        audit.Warn("account.deleted", context, user.Id, user.Email, "self-service");
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ registration

    private static async Task<IResult> Register(
        HttpContext context, RegisterRequest? request, UserManager<AppUser> userManager, AccountService accounts,
        IAccountEmailQueue emails, AuditLog audit, CancellationToken ct)
    {
        var email = request?.Email?.Trim() ?? string.Empty;
        var displayName = request?.DisplayName?.Trim();
        var password = request?.Password ?? string.Empty;

        // Validation never depends on whether the email is taken (no account enumeration).
        var errors = new Dictionary<string, string[]>();
        if (ValidateEmail(email) is { } emailError)
            errors["email"] = [emailError];
        if (ValidateDisplayName(displayName) is { } nameError)
            errors["displayName"] = [nameError];
        var passwordErrors = await ValidatePasswordAsync(userManager, password, email);
        if (passwordErrors.Length > 0)
            errors["password"] = passwordErrors;
        if (errors.Count > 0)
            return AuthSupport.Validation(errors);

        var existing = await userManager.FindByEmailAsync(email);
        if (existing is null)
        {
            var created = await accounts.CreateAsync(email, password, displayName!, ct);
            if (created.Result.Succeeded)
            {
                audit.Write("register", context, created.User.Id, email);
                await QueueConfirmationAsync(context, userManager, emails, created.User);
            }
            else if (created.Result.Errors.Any(e => e.Code is "DuplicateEmail" or "DuplicateUserName"))
            {
                existing = await userManager.FindByEmailAsync(email); // lost a race with a concurrent registration
            }
            else
            {
                return AuthSupport.Validation(MapIdentityErrors(created.Result));
            }
        }
        else
        {
            // Do the work a new registration does (hash the password — the dominant cost) so the
            // response time does not reveal that the address is taken.
            userManager.PasswordHasher.HashPassword(new AppUser(), password);
        }

        if (existing is not null)
        {
            audit.Write("register.existing_email", context, existing.Id, email);
            if (!existing.IsDisabled)
            {
                var baseUrl = AuthSupport.PublicBaseUrl(context);
                emails.Enqueue(new AccountEmail(existing.Email!, AccountEmailKind.AlreadyRegistered,
                    EmailTemplates.AlreadyRegistered(TrustedName(existing), $"{baseUrl}/login", $"{baseUrl}/forgot-password")));
            }
        }

        AuthSupport.IssueXsrfCookie(context);
        return Results.Accepted(value: new { });
    }

    /// <summary>
    /// Confirms the email address. Requires the account's password as well as the emailed token:
    /// whoever registered an address they do not own (pre-registration hijack) cannot get the real
    /// owner to activate it by clicking the link. Token first (a wrong/missing token never reveals
    /// anything about the password), then the password; a wrong password leaves the token usable.
    /// </summary>
    private static async Task<IResult> ConfirmEmail(
        HttpContext context, ConfirmEmailRequest? request, UserManager<AppUser> userManager, AccountService accounts,
        AuditLog audit, CancellationToken ct)
    {
        var token = AuthSupport.DecodeToken(request?.Token);
        var user = Guid.TryParse(request?.UserId, out var userId) ? await userManager.FindByIdAsync(userId.ToString()) : null;
        if (user is null || token is null || user.IsDisabled)
            return InvalidToken();

        var tokenValid = await userManager.VerifyUserTokenAsync(
            user, userManager.Options.Tokens.EmailConfirmationTokenProvider, UserManager<AppUser>.ConfirmEmailTokenPurpose, token);
        if (!tokenValid)
        {
            audit.Warn("email.confirm.failed", context, user.Id, user.Email, "invalid token");
            return InvalidToken();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            audit.Warn("email.confirm.failed", context, user.Id, user.Email, "locked out");
            return AuthSupport.Problem(StatusCodes.Status400BadRequest, "locked_out", "This account is temporarily locked. Try again later.");
        }

        var password = request?.Password ?? string.Empty;
        if (password.Length == 0 || password.Length > MaxPasswordLength || !await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            audit.Warn("email.confirm.failed", context, user.Id, user.Email, "wrong password");
            return WrongPassword();
        }

        var result = await accounts.ConfirmEmailAsync(user, token, ct);
        if (!result.Result.Succeeded)
        {
            audit.Warn("email.confirm.failed", context, user.Id, user.Email, "invalid token");
            return InvalidToken();
        }

        await userManager.ResetAccessFailedCountAsync(user);
        audit.Write(result.GrantedAdmin ? "email.confirmed.bootstrap_admin" : "email.confirmed", context, user.Id, user.Email);
        return Results.NoContent();
    }

    private static async Task<IResult> ResendConfirmation(
        HttpContext context, EmailRequest? request, UserManager<AppUser> userManager, IAccountEmailQueue emails,
        AuditLog audit)
    {
        var email = request?.Email?.Trim();
        if (!string.IsNullOrEmpty(email) && ValidateEmail(email) is null
            && await userManager.FindByEmailAsync(email) is { EmailConfirmed: false, IsDisabled: false } user)
        {
            audit.Write("email.confirmation_resent", context, user.Id, email);
            await QueueConfirmationAsync(context, userManager, emails, user);
        }
        return Results.Accepted(value: new { });
    }

    // ------------------------------------------------------------------ sign-in

    /// <summary>
    /// Non-enumerating: an unknown email, a wrong password and a disabled account with a wrong
    /// password all answer <c>invalid_credentials</c>, and after the same number of failures all
    /// answer the same <c>locked_out</c> (unknown emails are tracked by <see cref="UnknownAccountLockout"/>).
    /// <c>email_not_confirmed</c> and a disabled account's <c>locked_out</c> are only revealed to
    /// someone who knows the password.
    /// </summary>
    private static async Task<IResult> Login(
        HttpContext context, LoginRequest? request, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
        UnknownAccountLockout unknownLockout, AppDbContext db, IAppClockFactory clocks, TimeProvider time, AuditLog audit, CancellationToken ct)
    {
        var email = request?.Email?.Trim() ?? string.Empty;
        var password = request?.Password ?? string.Empty;
        var emailUsable = email.Length is > 0 and <= MaxEmailLength;

        var user = emailUsable ? await userManager.FindByEmailAsync(email) : null;
        if (user is null)
        {
            DummyPasswordCheck(userManager, password); // similar timing for unknown emails
            if (!emailUsable)
                return InvalidCredentials(); // no account can have this address
            var normalized = userManager.NormalizeEmail(email);
            if (unknownLockout.IsLockedOut(normalized))
            {
                audit.Warn("login.locked_out", context, null, email, "unknown email");
                return LockedOut();
            }
            if (unknownLockout.RecordFailure(normalized))
            {
                audit.Warn("login.failed", context, null, email, "unknown email; lockout started");
                return LockedOut();
            }
            audit.Warn("login.failed", context, null, email, "unknown email");
            return InvalidCredentials();
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            audit.Warn("login.locked_out", context, user.Id, email);
            return LockedOut();
        }

        if (password.Length == 0 || password.Length > MaxPasswordLength || !await userManager.CheckPasswordAsync(user, password))
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                audit.Warn("account.locked_out", context, user.Id, email, "too many failed sign-in attempts");
                return LockedOut();
            }
            audit.Warn("login.failed", context, user.Id, email, "wrong password");
            return InvalidCredentials();
        }

        // Only revealed once the password has been proven.
        if (user.IsDisabled)
        {
            audit.Warn("login.failed", context, user.Id, email, "account disabled by an administrator");
            return LockedOut();
        }
        if (!user.EmailConfirmed)
        {
            audit.Warn("login.failed", context, user.Id, email, "email not confirmed");
            return AuthSupport.Problem(StatusCodes.Status401Unauthorized, "email_not_confirmed", "Please confirm your email address first.");
        }

        await userManager.ResetAccessFailedCountAsync(user);
        user.LastLoginAt = time.GetUtcNow();
        await userManager.UpdateAsync(user);

        await signInManager.SignInAsync(user, new AuthenticationProperties { IsPersistent = request!.RememberMe }, "pwd");
        context.User = await signInManager.CreateUserPrincipalAsync(user);
        AuthSupport.IssueXsrfCookie(context);

        audit.Write("login.succeeded", context, user.Id, email, request.RememberMe ? "persistent" : "session");
        return Results.Ok(await AuthSupport.ToUserDtoAsync(user, userManager, db, clocks, ct));
    }

    /// <summary>
    /// Signs out and rotates the security stamp, so a copy of the session cookie (stolen, or left
    /// on another device) stops working at its next validation. This deliberately signs the user
    /// out on every device.
    /// </summary>
    private static async Task<IResult> Logout(HttpContext context, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, AuditLog audit)
    {
        var userId = AuthSupport.UserId(context.User);
        if (await userManager.GetUserAsync(context.User) is { } user)
            await userManager.UpdateSecurityStampAsync(user);
        await signInManager.SignOutAsync();
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        AuthSupport.IssueXsrfCookie(context);
        audit.Write("logout", context, userId, detail: "security stamp rotated (all sessions ended)");
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ passwords

    /// <summary>
    /// Mails a reset link to any non-disabled account — also an unconfirmed one: resetting with
    /// the emailed token proves control of the mailbox, so it confirms the address too. That is
    /// how the real owner recovers an address someone else registered (they cannot confirm it
    /// without the password that person chose).
    /// </summary>
    private static async Task<IResult> ForgotPassword(
        HttpContext context, EmailRequest? request, UserManager<AppUser> userManager, IAccountEmailQueue emails, AuditLog audit)
    {
        var email = request?.Email?.Trim();
        if (!string.IsNullOrEmpty(email) && ValidateEmail(email) is null && await userManager.FindByEmailAsync(email) is { } user)
        {
            if (user.IsDisabled)
            {
                audit.Warn("password.reset_refused", context, user.Id, email, "account disabled by an administrator");
            }
            else
            {
                var token = AuthSupport.EncodeToken(await userManager.GeneratePasswordResetTokenAsync(user));
                audit.Write(user.EmailConfirmed ? "password.reset_requested" : "password.reset_requested_unconfirmed", context, user.Id, email);
                emails.Enqueue(new AccountEmail(user.Email!, AccountEmailKind.ResetPassword,
                    EmailTemplates.ResetPassword(TrustedName(user), ResetLink(AuthSupport.PublicBaseUrl(context), user.Email!, token))));
            }
        }
        return Results.Accepted(value: new { });
    }

    private static async Task<IResult> ResetPassword(
        HttpContext context, ResetPasswordRequest? request, UserManager<AppUser> userManager, AccountService accounts,
        IAccountEmailQueue emails, AuditLog audit, CancellationToken ct)
    {
        var email = request?.Email?.Trim() ?? string.Empty;
        var newPassword = request?.NewPassword ?? string.Empty;

        var passwordErrors = await ValidatePasswordAsync(userManager, newPassword, email);
        if (passwordErrors.Length > 0)
            return AuthSupport.Validation(new Dictionary<string, string[]> { ["newPassword"] = passwordErrors });

        var token = AuthSupport.DecodeToken(request?.Token);
        var user = email.Length is > 0 and <= MaxEmailLength ? await userManager.FindByEmailAsync(email) : null;
        if (user is null || token is null)
            return InvalidToken();
        if (user.IsDisabled)
        {
            audit.Warn("password.reset_failed", context, user.Id, email, "account disabled by an administrator");
            return InvalidToken();
        }

        var result = await userManager.ResetPasswordAsync(user, token, newPassword); // also rotates the security stamp
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "InvalidToken"))
            {
                audit.Warn("password.reset_failed", context, user.Id, email, "invalid token");
                return InvalidToken();
            }
            return AuthSupport.Validation(new Dictionary<string, string[]> { ["newPassword"] = result.Errors.Select(e => e.Description).ToArray() });
        }

        // Proving control of the mailbox ends a brute-force lockout (never an admin disable — that
        // is IsDisabled, untouched here) and confirms a not-yet-confirmed address.
        await userManager.ResetAccessFailedCountAsync(user);
        await userManager.SetLockoutEndDateAsync(user, null);
        if (!user.EmailConfirmed)
        {
            var confirmed = await accounts.ConfirmEmailOwnershipProvenAsync(user, ct);
            audit.Write(confirmed.GrantedAdmin ? "email.confirmed.bootstrap_admin" : "email.confirmed", context, user.Id, email, "by password reset");
        }

        audit.Warn("password.reset", context, user.Id, email);
        emails.Enqueue(new AccountEmail(user.Email!, AccountEmailKind.PasswordChanged,
            EmailTemplates.PasswordChanged(TrustedName(user), $"{AuthSupport.PublicBaseUrl(context)}/forgot-password")));
        return Results.NoContent();
    }

    private static async Task<IResult> ChangePassword(
        HttpContext context, ChangePasswordRequest? request, UserManager<AppUser> userManager, SignInManager<AppUser> signInManager,
        IAccountEmailQueue emails, AuditLog audit)
    {
        var user = await userManager.GetUserAsync(context.User);
        if (user is null)
            return AuthSupport.Unauthenticated();

        var current = request?.CurrentPassword ?? string.Empty;
        var newPassword = request?.NewPassword ?? string.Empty;
        if (current.Length == 0 || current.Length > MaxPasswordLength || !await userManager.CheckPasswordAsync(user, current))
        {
            audit.Warn("password.change_failed", context, user.Id, user.Email, "wrong current password");
            return WrongPassword();
        }

        var passwordErrors = await ValidatePasswordAsync(userManager, newPassword, user.Email);
        if (passwordErrors.Length > 0)
            return AuthSupport.Validation(new Dictionary<string, string[]> { ["newPassword"] = passwordErrors });

        var result = await userManager.ChangePasswordAsync(user, current, newPassword); // rotates the security stamp
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
                return WrongPassword();
            return AuthSupport.Validation(new Dictionary<string, string[]> { ["newPassword"] = result.Errors.Select(e => e.Description).ToArray() });
        }

        // Re-issue this session's cookie with the new security stamp; other sessions die at their
        // next security-stamp validation.
        await signInManager.RefreshSignInAsync(user);
        context.User = await signInManager.CreateUserPrincipalAsync(user);
        AuthSupport.IssueXsrfCookie(context);

        audit.Warn("password.changed", context, user.Id, user.Email);
        emails.Enqueue(new AccountEmail(user.Email!, AccountEmailKind.PasswordChanged,
            EmailTemplates.PasswordChanged(TrustedName(user), $"{AuthSupport.PublicBaseUrl(context)}/forgot-password")));
        return Results.NoContent();
    }

    // ------------------------------------------------------------------ helpers

    private static IResult InvalidCredentials() =>
        AuthSupport.Problem(StatusCodes.Status401Unauthorized, "invalid_credentials", "Invalid email or password.");

    /// <summary>Re-entering a password wrongly outside sign-in (change password, delete account,
    /// confirm email): 400, not 401 — the session itself is fine.</summary>
    private static IResult WrongPassword() =>
        AuthSupport.Problem(StatusCodes.Status400BadRequest, "invalid_credentials", "The password is incorrect.");

    private static IResult LockedOut() =>
        AuthSupport.Problem(StatusCodes.Status401Unauthorized, "locked_out", "This account is temporarily locked. Try again later.");

    private static IResult InvalidToken() =>
        AuthSupport.Problem(StatusCodes.Status400BadRequest, "invalid_token", "The link is invalid or has expired.");

    public static string? ValidateEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "Email is required.";
        if (email.Length > MaxEmailLength)
            return $"Email must be at most {MaxEmailLength} characters.";
        if (!new EmailAddressAttribute().IsValid(email) || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            return "Email is not a valid email address.";
        return null;
    }

    public static string? ValidateDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "Display name is required.";
        if (displayName.Length > AppUser.DisplayNameMaxLength)
            return $"Display name must be at most {AppUser.DisplayNameMaxLength} characters.";
        if (displayName.Any(char.IsControl))
            return "Display name contains invalid characters.";
        return null;
    }

    private static async Task<string[]> ValidatePasswordAsync(UserManager<AppUser> userManager, string password, string? email)
    {
        if (string.IsNullOrEmpty(password))
            return ["Password is required."];
        var probe = new AppUser { UserName = email, Email = email };
        var errors = new List<string>();
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, probe, password);
            errors.AddRange(result.Errors.Select(e => e.Description));
        }
        return errors.Distinct().ToArray();
    }

    private static Dictionary<string, string[]> MapIdentityErrors(IdentityResult result) =>
        result.Errors
            .GroupBy(e => e.Code.StartsWith("Password", StringComparison.Ordinal) ? "password"
                : e.Code.Contains("Email", StringComparison.Ordinal) || e.Code.Contains("UserName", StringComparison.Ordinal) ? "email"
                : "general")
            .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

    private static string? _dummyHash;

    private static void DummyPasswordCheck(UserManager<AppUser> userManager, string password)
    {
        var dummy = new AppUser();
        _dummyHash ??= userManager.PasswordHasher.HashPassword(dummy, Guid.NewGuid().ToString());
        userManager.PasswordHasher.VerifyHashedPassword(dummy, _dummyHash, password.Length > MaxPasswordLength ? password[..MaxPasswordLength] : password);
    }

    /// <summary>The display name is only put into an email once the address is confirmed: before
    /// that it was typed by whoever registered, who may not own the mailbox.</summary>
    private static string? TrustedName(AppUser user) => user.EmailConfirmed ? user.DisplayName : null;

    /// <summary>
    /// Email links carry the token in the URL fragment (<c>#userId=..&amp;token=..</c>): browsers
    /// never send the fragment to a server, so it stays out of access logs, proxies and Referer.
    /// </summary>
    public static string ConfirmLink(string baseUrl, Guid userId, string encodedToken) =>
        $"{baseUrl}/confirm-email#userId={userId}&token={encodedToken}";

    public static string ResetLink(string baseUrl, string email, string encodedToken) =>
        $"{baseUrl}/reset-password#email={Uri.EscapeDataString(email)}&token={encodedToken}";

    /// <summary>Queues a confirmation email (neutral greeting — the address is unconfirmed).</summary>
    internal static async Task QueueConfirmationAsync(HttpContext context, UserManager<AppUser> userManager, IAccountEmailQueue emails, AppUser user)
    {
        var token = AuthSupport.EncodeToken(await userManager.GenerateEmailConfirmationTokenAsync(user));
        emails.Enqueue(new AccountEmail(user.Email!, AccountEmailKind.ConfirmEmail,
            EmailTemplates.ConfirmEmail(ConfirmLink(AuthSupport.PublicBaseUrl(context), user.Id, token))));
    }
}
