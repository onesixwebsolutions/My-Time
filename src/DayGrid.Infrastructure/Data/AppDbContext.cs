using System.Reflection;
using DayGrid.Application.Security;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DayGrid.Infrastructure.Data;

/// <summary>
/// EF Core context: the DayGrid domain, ASP.NET Core Identity (users/roles, snake_case tables)
/// and the Data Protection key ring.
///
/// <para><b>Tenant isolation.</b> Every <see cref="IUserOwned"/> entity has a global query filter
/// restricting it to <see cref="ICurrentUser.UserId"/>; with no current user, those queries return
/// nothing. On save, inserted rows are stamped with the current user, and inserts/updates are
/// rejected (<see cref="TenantViolationException"/>) when they would write another user's row,
/// change a row's owner, or point a foreign key (checklistId, templateId, timetableBlockId, ...)
/// at a row owned by somebody else.</para>
///
/// <para>A context constructed <i>without</i> an <see cref="ICurrentUser"/> (design-time tooling,
/// tests, maintenance scripts) is a "system" context: no filtering, no stamping. The application
/// container always constructs it with one.</para>
/// </summary>
public class AppDbContext : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>, IDataProtectionKeyContext
{
    private readonly ICurrentUser? _currentUser;

    /// <summary>System context — no tenant filtering (tooling and tests only).</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>Tenant-scoped context, as resolved from the application container.</summary>
    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUser currentUser) : base(options)
    {
        _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
    }

    // Referenced by the query filters: EF evaluates these per query execution, per context instance.
    private bool TenantFilterEnabled => _currentUser is not null;
    private Guid? CurrentUserId => _currentUser?.UserId;

    public DbSet<Checklist> Checklists => Set<Checklist>();
    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();
    public DbSet<ChecklistCompletion> ChecklistCompletions => Set<ChecklistCompletion>();

    public DbSet<TimetableTemplate> TimetableTemplates => Set<TimetableTemplate>();
    public DbSet<TimetableBlock> TimetableBlocks => Set<TimetableBlock>();
    public DbSet<TimetableAssignment> TimetableAssignments => Set<TimetableAssignment>();
    public DbSet<DayOverride> DayOverrides => Set<DayOverride>();

    public DbSet<FutureTask> FutureTasks => Set<FutureTask>();
    public DbSet<Reminder> Reminders => Set<Reminder>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    public DbSet<SimpleTask> SimpleTasks => Set<SimpleTask>();

    public DbSet<ConstantExpense> ConstantExpenses => Set<ConstantExpense>();
    public DbSet<VaryingExpense> VaryingExpenses => Set<VaryingExpense>();
    public DbSet<CompletedSpend> CompletedSpends => Set<CompletedSpend>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    /// <summary>Every user-owned entity type, in a deterministic order.</summary>
    public static readonly IReadOnlyList<Type> UserOwnedTypes =
        typeof(IUserOwned).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IUserOwned).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

    private static readonly MethodInfo ConfigureUserOwnedMethod =
        typeof(AppDbContext).GetMethod(nameof(ConfigureUserOwned), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static readonly MethodInfo PrincipalOwnedByMethod =
        typeof(AppDbContext).GetMethod(nameof(PrincipalOwnedByAsync), BindingFlags.Instance | BindingFlags.NonPublic)!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var type in UserOwnedTypes)
            ConfigureUserOwnedMethod.MakeGenericMethod(type).Invoke(this, [modelBuilder]);
    }

    private void ConfigureUserOwned<T>(ModelBuilder modelBuilder) where T : class, IUserOwned
    {
        var entity = modelBuilder.Entity<T>();
        entity.Property(e => e.UserId).HasColumnName("user_id");
        entity.HasOne<AppUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        entity.HasQueryFilter(e => !TenantFilterEnabled || (CurrentUserId != null && e.UserId == CurrentUserId));
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyTenantRulesAsync(async: false, CancellationToken.None).GetAwaiter().GetResult();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        await ApplyTenantRulesAsync(async: true, cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private async Task ApplyTenantRulesAsync(bool async, CancellationToken ct)
    {
        ChangeTracker.DetectChanges();
        var enforce = _currentUser is not null;
        var actor = CurrentUserId;

        var owned = ChangeTracker.Entries<IUserOwned>()
            .Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();
        if (owned.Count == 0)
            return;

        foreach (var entry in owned)
        {
            var entity = entry.Entity;
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entity.UserId is null)
                    {
                        if (enforce && actor is null)
                            throw new TenantViolationException($"Cannot insert {entry.Metadata.ClrType.Name} without a current user.", isReference: false);
                        entity.UserId = actor;
                    }
                    else if (enforce && actor is not null && entity.UserId != actor)
                    {
                        throw new TenantViolationException($"Cannot insert {entry.Metadata.ClrType.Name} for another user.", isReference: false);
                    }
                    break;

                case EntityState.Modified:
                    var owner = entry.Property(nameof(IUserOwned.UserId));
                    if (owner.IsModified)
                    {
                        // Only a system context may assign an owner, and only to a legacy (unowned) row.
                        if (enforce || owner.OriginalValue is not null)
                            throw new TenantViolationException($"The owner of a {entry.Metadata.ClrType.Name} cannot be changed.", isReference: false);
                        break;
                    }
                    goto case EntityState.Deleted;

                case EntityState.Deleted:
                    if (enforce && entity.UserId != actor)
                        throw new TenantViolationException($"Cannot modify another user's {entry.Metadata.ClrType.Name}.", isReference: false);
                    break;
            }
        }

        // Foreign keys between user-owned rows must stay within one owner.
        var tracked = ChangeTracker.Entries<IUserOwned>()
            .Where(e => e.State != EntityState.Detached && e.Metadata.FindPrimaryKey()!.Properties.Count == 1)
            .GroupBy(e => (e.Metadata.ClrType, Key: e.Property(e.Metadata.FindPrimaryKey()!.Properties[0].Name).CurrentValue))
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var entry in owned.Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var fk in entry.Metadata.GetForeignKeys())
            {
                var principalType = fk.PrincipalEntityType.ClrType;
                if (!typeof(IUserOwned).IsAssignableFrom(principalType) || fk.Properties.Count != 1)
                    continue;

                var fkProperty = entry.Property(fk.Properties[0].Name);
                if (fkProperty.CurrentValue is not Guid referencedId)
                    continue;
                if (entry.State == EntityState.Modified && !fkProperty.IsModified)
                    continue;

                if (await IsOwnedByAsync(principalType, referencedId, entry.Entity.UserId, tracked, async, ct))
                    continue;

                throw new TenantViolationException(
                    $"{entry.Metadata.ClrType.Name}.{fk.Properties[0].Name} references a {principalType.Name} that does not exist.",
                    isReference: true);
            }
        }
    }

    private async Task<bool> IsOwnedByAsync(
        Type principalType, Guid id, Guid? owner,
        Dictionary<(Type, object?), EntityEntry<IUserOwned>> tracked, bool async, CancellationToken ct)
    {
        if (tracked.TryGetValue((principalType, id), out var principalEntry))
            return principalEntry.State != EntityState.Deleted && principalEntry.Entity.UserId == owner;

        return await (Task<bool>)PrincipalOwnedByMethod.MakeGenericMethod(principalType).Invoke(this, [id, owner, async, ct])!;
    }

    private async Task<bool> PrincipalOwnedByAsync<T>(Guid id, Guid? owner, bool async, CancellationToken ct) where T : class, IUserOwned
    {
        var query = Set<T>().IgnoreQueryFilters().AsNoTracking()
            .Where(e => EF.Property<Guid>(e, "Id") == id && e.UserId == owner);
        return async ? await query.AnyAsync(ct) : query.Any();
    }
}

/// <summary>
/// A write that would cross the tenant boundary. <see cref="IsReference"/> is true when a foreign
/// key points at a row the writer does not own (reported to clients like a dangling reference).
/// </summary>
public sealed class TenantViolationException : InvalidOperationException
{
    public TenantViolationException(string message, bool isReference) : base(message)
    {
        IsReference = isReference;
    }

    public bool IsReference { get; }
}
