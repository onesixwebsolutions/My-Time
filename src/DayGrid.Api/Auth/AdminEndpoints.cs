using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Auth;

/// <summary>
/// /api/v1/admin — account management for the Admin role. Deliberately exposes account metadata
/// only: there is no endpoint through which an admin can read another user's DayGrid data.
/// </summary>
public static class AdminEndpoints
{
    public const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin").WithTags("Admin").RequireAuthorization(AuthSupport.AdminPolicy);

        group.MapGet("/users", ListUsers).WithName("AdminListUsers");
        group.MapPost("/users/{id:guid}/lock", Lock).WithName("AdminLockUser");
        group.MapPost("/users/{id:guid}/unlock", Unlock).WithName("AdminUnlockUser");
        group.MapPost("/users/{id:guid}/resend-confirmation", ResendConfirmation).WithName("AdminResendConfirmation");
        group.MapDelete("/users/{id:guid}", Delete).WithName("AdminDeleteUser");

        return app;
    }

    private static async Task<IResult> ListUsers(
        AppDbContext db, TimeProvider time, string? search, int? page, int? pageSize, CancellationToken ct)
    {
        var pageNumber = Math.Max(page ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 20, 1, MaxPageSize);

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var upper = term.ToUpperInvariant();
            var lower = term.ToLowerInvariant();
            query = query.Where(u => (u.NormalizedEmail != null && u.NormalizedEmail.Contains(upper)) || u.DisplayName.ToLower().Contains(lower));
        }

        var total = await query.CountAsync(ct);
        var users = await query
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where ids.Contains(ur.UserId)
                           select new { ur.UserId, r.Name })
            .ToListAsync(ct);
        var rolesByUser = roles.GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name!).OrderBy(n => n, StringComparer.Ordinal).ToList());

        var now = time.GetUtcNow();
        var items = users.Select(u => new AdminUserDto(
            u.Id, u.Email ?? string.Empty, u.DisplayName,
            rolesByUser.TryGetValue(u.Id, out var r) ? r : [],
            u.EmailConfirmed,
            // Locked = disabled by an admin, or in a brute-force lockout right now; "unlock" clears both.
            u.IsDisabled || (u.LockoutEnd is { } end && end > now),
            u.IsDisabled,
            u.CreatedAt, u.LastLoginAt)).ToList();

        return Results.Ok(new { items, total });
    }

    private static async Task<IResult> Lock(Guid id, HttpContext context, UserManager<AppUser> userManager, AuditLog audit)
    {
        if (AuthSupport.UserId(context.User) == id)
            return AuthSupport.Problem(StatusCodes.Status400BadRequest, "cannot_lock_self", "You cannot lock your own account.");
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();

        // A distinct "disabled" state (not LockoutEnd, which a password reset clears). Rotating the
        // security stamp ends existing sessions at their next validation and invalidates any
        // outstanding reset/confirmation tokens.
        user.IsDisabled = true;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not disable the account: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        await userManager.UpdateSecurityStampAsync(user);
        audit.Warn("admin.user_locked", context, AuthSupport.UserId(context.User), user.Email, $"target {user.Id}");
        return Results.NoContent();
    }

    private static async Task<IResult> Unlock(Guid id, HttpContext context, UserManager<AppUser> userManager, AuditLog audit)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();

        user.IsDisabled = false;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not enable the account: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        audit.Write("admin.user_unlocked", context, AuthSupport.UserId(context.User), user.Email, $"target {user.Id}");
        return Results.NoContent();
    }

    private static async Task<IResult> ResendConfirmation(
        Guid id, HttpContext context, UserManager<AppUser> userManager, IAccountEmailQueue emails, AuditLog audit)
    {
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();

        if (!user.EmailConfirmed && !user.IsDisabled)
        {
            await AuthEndpoints.QueueConfirmationAsync(context, userManager, emails, user);
            audit.Write("admin.confirmation_resent", context, AuthSupport.UserId(context.User), user.Email, $"target {user.Id}");
        }
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(
        Guid id, HttpContext context, UserManager<AppUser> userManager, AccountService accounts, AuditLog audit, CancellationToken ct)
    {
        if (AuthSupport.UserId(context.User) == id)
            return AuthSupport.Problem(StatusCodes.Status400BadRequest, "cannot_delete_self", "You cannot delete your own account here.");
        var user = await userManager.FindByIdAsync(id.ToString());
        if (user is null)
            return Results.NotFound();

        var result = await accounts.DeleteAsync(user, ct);
        if (!result.Succeeded)
            throw new InvalidOperationException("Account deletion failed: " + string.Join("; ", result.Errors.Select(e => e.Code)));
        audit.Warn("admin.user_deleted", context, AuthSupport.UserId(context.User), user.Email, $"target {user.Id}");
        return Results.NoContent();
    }
}
