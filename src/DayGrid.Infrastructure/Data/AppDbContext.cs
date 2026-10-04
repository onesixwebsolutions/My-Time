using DayGrid.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
