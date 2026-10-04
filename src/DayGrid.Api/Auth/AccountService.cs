using System.Reflection;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Auth;

/// <summary>
/// Account lifecycle operations that span Identity and the tenant data: creating an account,
/// confirming its email address (including the race-safe bootstrap-Admin rule, which claims the
/// legacy data) and deleting an account together with every row it owns.
/// </summary>
public sealed class AccountService
{
    /// <summary>Key for pg_advisory_xact_lock: serialises account creation and email confirmation
    /// (the bootstrap-Admin decision) across app instances.</summary>
    private const string AccountLockSql = "SELECT pg_advisory_xact_lock(4927352815089181698)"; // 0x4461794772696402

    /// <summary>In-process gate (single instance, and the EF InMemory provider used by tests).</summary>
    private static readonly SemaphoreSlim AccountGate = new(1, 1);

    private static readonly MethodInfo ClaimInMemoryMethod =
        typeof(AccountService).GetMethod(nameof(ClaimLegacyRowsInMemoryAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo DeleteInMemoryMethod =
        typeof(AccountService).GetMethod(nameof(DeleteOwnedRowsInMemoryAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IAppClockFactory _clockFactory;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly TimeProvider _timeProvider;
    private readonly BootstrapAdminPolicy _bootstrap;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        AppDbContext db, UserManager<AppUser> userManager, IAppClockFactory clockFactory,
        DbContextOptions<AppDbContext> dbOptions, TimeProvider timeProvider, BootstrapAdminPolicy bootstrap,
        ILogger<AccountService> logger)
    {
        _db = db;
        _userManager = userManager;
        _clockFactory = clockFactory;
        _dbOptions = dbOptions;
        _timeProvider = timeProvider;
        _bootstrap = bootstrap;
        _logger = logger;
    }

    public sealed record CreateResult(IdentityResult Result, AppUser User);

    public sealed record ConfirmResult(IdentityResult Result, bool GrantedAdmin);

    /// <summary>
    /// Creates an (unconfirmed) account with role User and a default settings row, in one
    /// transaction. No account is ever granted Admin here: that happens at email confirmation
    /// (<see cref="ConfirmEmailAsync"/>), see <see cref="BootstrapAdminPolicy"/>.
    /// </summary>
    public async Task<CreateResult> CreateAsync(string email, string password, string displayName, CancellationToken ct)
    {
        await AccountGate.WaitAsync(ct);
        try
        {
            var relational = _db.Database.IsRelational();
            await using var transaction = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
            if (relational && _db.Database.IsNpgsql())
                await _db.Database.ExecuteSqlRawAsync(AccountLockSql, ct);

            await EnsureRolesAsync(ct);
            var user = new AppUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                DisplayName = displayName,
                CreatedAt = _timeProvider.GetUtcNow()
            };

            var result = await _userManager.CreateAsync(user, password);
            if (!result.Succeeded)
                return new CreateResult(result, user); // transaction rolls back on dispose

            result = await _userManager.AddToRoleAsync(user, AppRoles.User);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not assign roles: " + string.Join("; ", result.Errors.Select(e => e.Code)));

            if (!await _db.AppSettings.IgnoreQueryFilters().AnyAsync(s => s.UserId == user.Id, ct))
            {
                _db.AppSettings.Add(new AppSetting
                {
                    UserId = user.Id,
                    TimeZone = _clockFactory.DefaultTimeZoneId,
                    EmailTo = email
                });
                await _db.SaveChangesAsync(ct);
            }

            if (transaction is not null)
                await transaction.CommitAsync(ct);

            return new CreateResult(IdentityResult.Success, user);
        }
        finally
        {
            AccountGate.Release();
        }
    }

    /// <summary>
    /// Confirms the email address with the emailed token. In the same transaction (under the
    /// account lock, so two simultaneous confirmations cannot both become the bootstrap Admin) the
    /// bootstrap-Admin rule is applied and the security stamp is rotated — which also makes the
    /// confirmation token single-use.
    /// </summary>
    public Task<ConfirmResult> ConfirmEmailAsync(AppUser user, string token, CancellationToken ct) =>
        CompleteConfirmationAsync(user, () => _userManager.ConfirmEmailAsync(user, token), ct);

    /// <summary>
    /// Marks the address confirmed after the user proved control of the mailbox another way (a
    /// successful password reset with an emailed token). Same bootstrap-Admin rule and stamp rotation.
    /// </summary>
    public Task<ConfirmResult> ConfirmEmailOwnershipProvenAsync(AppUser user, CancellationToken ct) =>
        CompleteConfirmationAsync(user, async () =>
        {
            user.EmailConfirmed = true;
            return await _userManager.UpdateAsync(user);
        }, ct);

    private async Task<ConfirmResult> CompleteConfirmationAsync(AppUser user, Func<Task<IdentityResult>> confirm, CancellationToken ct)
    {
        await AccountGate.WaitAsync(ct);
        try
        {
            var relational = _db.Database.IsRelational();
            await using var transaction = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
            if (relational && _db.Database.IsNpgsql())
                await _db.Database.ExecuteSqlRawAsync(AccountLockSql, ct);

            var result = await confirm();
            if (!result.Succeeded)
                return new ConfirmResult(result, false); // transaction rolls back on dispose

            var grantedAdmin = await ApplyBootstrapAdminAsync(user, relational, ct);

            result = await _userManager.UpdateSecurityStampAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not rotate the security stamp: " + string.Join("; ", result.Errors.Select(e => e.Code)));

            if (transaction is not null)
                await transaction.CommitAsync(ct);
            return new ConfirmResult(IdentityResult.Success, grantedAdmin);
        }
        finally
        {
            AccountGate.Release();
        }
    }

    private async Task<bool> ApplyBootstrapAdminAsync(AppUser user, bool relational, CancellationToken ct)
    {
        if (!_bootstrap.Matches(user.NormalizedEmail))
            return false;

        await EnsureRolesAsync(ct);
        if (await _userManager.IsInRoleAsync(user, AppRoles.Admin))
            return false;

        if (_bootstrap.Mode == BootstrapAdminMode.FirstConfirmedUser)
        {
            var adminRole = AppRoles.Admin.ToUpperInvariant();
            var anyAdmin = await (from ur in _db.UserRoles
                                  join r in _db.Roles on ur.RoleId equals r.Id
                                  where r.NormalizedName == adminRole
                                  select ur.UserId).AnyAsync(ct);
            if (anyAdmin)
                return false;
        }

        var result = await _userManager.AddToRoleAsync(user, AppRoles.Admin);
        if (!result.Succeeded)
            throw new InvalidOperationException("Could not grant Admin: " + string.Join("; ", result.Errors.Select(e => e.Code)));

        var claimed = await ClaimLegacyDataAsync(user.Id, relational, ct);
        _logger.LogWarning("Bootstrap admin ({Mode}): account {UserId} confirmed its email, was granted Admin and claimed {Count} legacy rows",
            _bootstrap.Mode, user.Id, claimed);
        return true;
    }

    /// <summary>Migration 0002 seeds both roles; this only matters for a database created some
    /// other way (e.g. the EF InMemory provider in tests).</summary>
    private async Task EnsureRolesAsync(CancellationToken ct)
    {
        var added = false;
        foreach (var (id, name) in new[] { (AppRoles.UserRoleId, AppRoles.User), (AppRoles.AdminRoleId, AppRoles.Admin) })
        {
            var normalized = name.ToUpperInvariant();
            if (await _db.Roles.AnyAsync(r => r.NormalizedName == normalized, ct))
                continue;
            _db.Roles.Add(new IdentityRole<Guid> { Id = id, Name = name, NormalizedName = normalized, ConcurrencyStamp = id.ToString() });
            added = true;
        }
        if (added)
            await _db.SaveChangesAsync(ct);
    }

    /// <summary>Assigns every legacy (user_id NULL) row to <paramref name="userId"/>. A legacy
    /// settings row replaces the default one created at registration.</summary>
    private async Task<int> ClaimLegacyDataAsync(Guid userId, bool relational, CancellationToken ct)
    {
        var claimed = 0;
        if (relational)
        {
            // app_settings is one row per user: a legacy row (the old single-user settings) wins
            // over the default row this account got at registration.
            if (await _db.AppSettings.IgnoreQueryFilters().AnyAsync(s => s.UserId == null, ct))
                await _db.Database.ExecuteSqlRawAsync("DELETE FROM app_settings WHERE user_id = {0}", [userId], ct);

            foreach (var type in AppDbContext.UserOwnedTypes)
            {
                // The table name is EF model metadata (never user input); the user id is a parameter.
                var table = _db.Model.FindEntityType(type)!.GetTableName()!;
                var sql = "UPDATE \"" + table + "\" SET user_id = {0} WHERE user_id IS NULL";
                claimed += await _db.Database.ExecuteSqlRawAsync(sql, [userId], ct);
            }
        }
        else
        {
            await using var system = new AppDbContext(_dbOptions);
            if (await system.AppSettings.AnyAsync(s => s.UserId == null, ct))
                system.AppSettings.RemoveRange(await system.AppSettings.Where(s => s.UserId == userId).ToListAsync(ct));
            foreach (var type in AppDbContext.UserOwnedTypes)
                claimed += await (Task<int>)ClaimInMemoryMethod.MakeGenericMethod(type).Invoke(null, [system, userId, ct])!;
            await system.SaveChangesAsync(ct);
        }
        return claimed;
    }

    private static async Task<int> ClaimLegacyRowsInMemoryAsync<T>(AppDbContext system, Guid userId, CancellationToken ct)
        where T : class, IUserOwned
    {
        var rows = await system.Set<T>().Where(e => e.UserId == null).ToListAsync(ct);
        foreach (var row in rows)
            row.UserId = userId;
        return rows.Count;
    }

    /// <summary>Deletes the account and every row it owns (FK ON DELETE CASCADE from users).</summary>
    public async Task<IdentityResult> DeleteAsync(AppUser user, CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var result = await _userManager.DeleteAsync(user); // the database cascades to all owned rows
            if (result.Succeeded)
                await transaction.CommitAsync(ct);
            return result;
        }

        // EF InMemory has no database-side cascade: remove the owned rows explicitly.
        await using (var system = new AppDbContext(_dbOptions))
        {
            foreach (var type in AppDbContext.UserOwnedTypes)
                await (Task)DeleteInMemoryMethod.MakeGenericMethod(type).Invoke(null, [system, user.Id, ct])!;
            await system.SaveChangesAsync(ct);
        }
        return await _userManager.DeleteAsync(user);
    }

    private static async Task DeleteOwnedRowsInMemoryAsync<T>(AppDbContext system, Guid userId, CancellationToken ct)
        where T : class, IUserOwned
    {
        system.Set<T>().RemoveRange(await system.Set<T>().Where(e => e.UserId == userId).ToListAsync(ct));
    }
}
