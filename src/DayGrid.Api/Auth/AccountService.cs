using System.Reflection;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Auth;

/// <summary>
/// Account lifecycle operations that span Identity and the tenant data: creating an account
/// (including the race-safe "first account claims the legacy data and becomes Admin" rule) and
/// deleting an account together with every row it owns.
/// </summary>
public sealed class AccountService
{
    /// <summary>Key for pg_advisory_xact_lock: serialises registrations across app instances.</summary>
    private const string RegistrationLockSql = "SELECT pg_advisory_xact_lock(4927352815089181698)"; // 0x4461794772696402

    /// <summary>In-process gate (single instance, and the EF InMemory provider used by tests).</summary>
    private static readonly SemaphoreSlim RegistrationGate = new(1, 1);

    private static readonly MethodInfo ClaimInMemoryMethod =
        typeof(AccountService).GetMethod(nameof(ClaimLegacyRowsInMemoryAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo DeleteInMemoryMethod =
        typeof(AccountService).GetMethod(nameof(DeleteOwnedRowsInMemoryAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly AppDbContext _db;
    private readonly UserManager<AppUser> _userManager;
    private readonly IAppClockFactory _clockFactory;
    private readonly DbContextOptions<AppDbContext> _dbOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        AppDbContext db, UserManager<AppUser> userManager, IAppClockFactory clockFactory,
        DbContextOptions<AppDbContext> dbOptions, TimeProvider timeProvider, ILogger<AccountService> logger)
    {
        _db = db;
        _userManager = userManager;
        _clockFactory = clockFactory;
        _dbOptions = dbOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public sealed record CreateResult(IdentityResult Result, AppUser User, bool IsFirstUser);

    /// <summary>
    /// Creates an (unconfirmed) account with role User and a default settings row. If no account
    /// exists yet, the new account also gets role Admin and every legacy row (user_id NULL) is
    /// assigned to it — all in one transaction, under a lock, so two simultaneous "first"
    /// registrations cannot both become Admin.
    /// </summary>
    public async Task<CreateResult> CreateAsync(string email, string password, string displayName, CancellationToken ct)
    {
        await RegistrationGate.WaitAsync(ct);
        try
        {
            var relational = _db.Database.IsRelational();
            await using var transaction = relational ? await _db.Database.BeginTransactionAsync(ct) : null;
            if (relational && _db.Database.IsNpgsql())
                await _db.Database.ExecuteSqlRawAsync(RegistrationLockSql, ct);

            await EnsureRolesAsync(ct);
            var isFirst = !await _db.Users.AnyAsync(ct);
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
                return new CreateResult(result, user, false); // transaction rolls back on dispose

            result = await _userManager.AddToRoleAsync(user, AppRoles.User);
            if (result.Succeeded && isFirst)
                result = await _userManager.AddToRoleAsync(user, AppRoles.Admin);
            if (!result.Succeeded)
                throw new InvalidOperationException("Could not assign roles: " + string.Join("; ", result.Errors.Select(e => e.Code)));

            if (isFirst)
                await ClaimLegacyDataAsync(user.Id, relational, ct);

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

            return new CreateResult(IdentityResult.Success, user, isFirst);
        }
        finally
        {
            RegistrationGate.Release();
        }
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

    private async Task ClaimLegacyDataAsync(Guid userId, bool relational, CancellationToken ct)
    {
        var claimed = 0;
        if (relational)
        {
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
            foreach (var type in AppDbContext.UserOwnedTypes)
                claimed += await (Task<int>)ClaimInMemoryMethod.MakeGenericMethod(type).Invoke(null, [system, userId, ct])!;
            await system.SaveChangesAsync(ct);
        }

        _logger.LogWarning("First account {UserId} created: granted Admin and claimed {Count} legacy rows", userId, claimed);
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
