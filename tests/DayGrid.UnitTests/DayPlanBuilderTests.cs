using Microsoft.EntityFrameworkCore;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Scheduling;
using Xunit;

namespace DayGrid.UnitTests;

public class DayPlanBuilderTests
{
    // 2026-10-05 is a Monday.
    private static readonly DateOnly Monday = new(2026, 10, 5);

    private static DayPlanBuilder Builder(AppDbContext db, DateOnly today, TimeOnly time) =>
        new(db, FixedAppClock.At(today, time));

    private static TimetableTemplate Template(AppDbContext db, string name, bool isDefault = false)
    {
        var t = new TimetableTemplate { Name = name, IsDefault = isDefault };
        db.TimetableTemplates.Add(t);
        return t;
    }

    private static void Assign(AppDbContext db, TimetableTemplate t, AssignmentScope scope,
        DayOfWeek? dow = null, DateOnly? from = null, DateOnly? to = null, int priority = 0) =>
        db.TimetableAssignments.Add(new TimetableAssignment
        {
            TemplateId = t.Id, Scope = scope, DayOfWeek = dow, DateFrom = from, DateTo = to, Priority = priority
        });

    // ------------------------------------------------------------------ empty / shape

    [Fact]
    public async Task EmptyDatabase_ReturnsEmptyPlanWithPlaceholderTemplate()
    {
        using var db = TestDb.Create();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(Monday, plan.Date);
        Assert.Equal("Monday", plan.DayOfWeek);
        Assert.Null(plan.Override);
        Assert.Equal(Guid.Empty, plan.Template.Id);
        Assert.Equal("(none)", plan.Template.Name);
        Assert.Equal(new TimeOnly(0, 0), plan.Template.DayStart);
        Assert.Equal(new TimeOnly(23, 59), plan.Template.DayEnd);
        Assert.Equal(30, plan.Template.SlotMinutes);
        Assert.Null(plan.NowBlock);
        Assert.Null(plan.NextBlock);
        Assert.Empty(plan.Blocks);
        Assert.Empty(plan.Checklists);
        Assert.Empty(plan.DueToday);
        Assert.Empty(plan.Overdue);
        Assert.Equal(0, plan.Summary.TotalItems);
        Assert.Equal(0, plan.Summary.CompletionPercent);
        Assert.Equal(0, plan.Summary.BlocksTotal);
        Assert.Equal(0, plan.Summary.MinutesScheduled);
    }

    // ------------------------------------------------------------------ template resolution

    [Fact]
    public async Task NoAssignments_UsesDefaultTemplate()
    {
        using var db = TestDb.Create();
        Template(db, "Other");
        var def = Template(db, "Default", isDefault: true);
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(def.Id, plan.Template.Id);
    }

    [Fact]
    public async Task WeekdayAssignment_BeatsDefault_OnlyOnMatchingWeekday()
    {
        using var db = TestDb.Create();
        var def = Template(db, "Default", isDefault: true);
        var weekday = Template(db, "Mondays");
        Assign(db, weekday, AssignmentScope.Weekday, dow: DayOfWeek.Monday);
        await db.SaveChangesAsync();
        var builder = Builder(db, Monday, new TimeOnly(10, 0));

        Assert.Equal(weekday.Id, (await builder.BuildAsync(Monday)).Template.Id);
        Assert.Equal(def.Id, (await builder.BuildAsync(Monday.AddDays(1))).Template.Id);
    }

    [Fact]
    public async Task DateRangeAssignment_BeatsWeekday_InsideRangeInclusive()
    {
        using var db = TestDb.Create();
        var weekday = Template(db, "Mondays");
        var range = Template(db, "Holiday week");
        Assign(db, weekday, AssignmentScope.Weekday, dow: DayOfWeek.Monday, priority: 100);
        Assign(db, range, AssignmentScope.DateRange, from: Monday, to: Monday.AddDays(6));
        await db.SaveChangesAsync();
        var builder = Builder(db, Monday, new TimeOnly(10, 0));

        Assert.Equal(range.Id, (await builder.BuildAsync(Monday)).Template.Id);
        Assert.Equal(range.Id, (await builder.BuildAsync(Monday.AddDays(6))).Template.Id);
        Assert.Equal(weekday.Id, (await builder.BuildAsync(Monday.AddDays(7))).Template.Id);
    }

    [Fact]
    public async Task SpecificDateAssignment_BeatsDateRange()
    {
        using var db = TestDb.Create();
        var range = Template(db, "Range");
        var specific = Template(db, "Specific");
        Assign(db, range, AssignmentScope.DateRange, from: Monday.AddDays(-3), to: Monday.AddDays(3), priority: 100);
        Assign(db, specific, AssignmentScope.SpecificDate, from: Monday);
        await db.SaveChangesAsync();
        var builder = Builder(db, Monday, new TimeOnly(10, 0));

        Assert.Equal(specific.Id, (await builder.BuildAsync(Monday)).Template.Id);
        Assert.Equal(range.Id, (await builder.BuildAsync(Monday.AddDays(1))).Template.Id);
    }

    [Fact]
    public async Task WithinSameScope_HigherPriorityWins()
    {
        using var db = TestDb.Create();
        var low = Template(db, "Low");
        var high = Template(db, "High");
        Assign(db, low, AssignmentScope.Weekday, dow: DayOfWeek.Monday, priority: 1);
        Assign(db, high, AssignmentScope.Weekday, dow: DayOfWeek.Monday, priority: 5);
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(high.Id, plan.Template.Id);
    }

    [Fact]
    public async Task DayOverrideUseTemplate_BeatsSpecificDateAssignment()
    {
        using var db = TestDb.Create();
        var specific = Template(db, "Specific");
        var overrideTemplate = Template(db, "Override");
        Assign(db, specific, AssignmentScope.SpecificDate, from: Monday);
        db.DayOverrides.Add(new DayOverride { Date = Monday, Mode = DayOverrideMode.UseTemplate, TemplateId = overrideTemplate.Id, Note = "swap" });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(overrideTemplate.Id, plan.Template.Id);
        Assert.NotNull(plan.Override);
        Assert.Equal("UseTemplate", plan.Override!.Mode);
        Assert.Equal("swap", plan.Override.Note);
    }

    [Fact]
    public async Task DayOverrideUseTemplate_WithDeletedTemplate_FallsThroughToNormalResolution()
    {
        var dbName = Guid.NewGuid().ToString();
        using var db = TestDb.Create(dbName);
        var def = Template(db, "Default", isDefault: true);
        var doomed = Template(db, "Doomed");
        db.DayOverrides.Add(new DayOverride { Date = Monday, Mode = DayOverrideMode.UseTemplate, TemplateId = doomed.Id });
        await db.SaveChangesAsync();

        // Delete the template from another context: InMemory does not cascade to untracked rows,
        // leaving a dangling override exactly like a stale reference would look.
        using (var other = TestDb.Create(dbName))
        {
            other.TimetableTemplates.Remove(await other.TimetableTemplates.SingleAsync(t => t.Id == doomed.Id));
            await other.SaveChangesAsync();
        }

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(def.Id, plan.Template.Id);
    }

    [Theory]
    [InlineData(DayOverrideMode.RestDay)]
    [InlineData(DayOverrideMode.CustomOnly)]
    public async Task RestDayOrCustomOnlyOverride_HidesTemplateBlocks_ButKeepsChecklists(DayOverrideMode mode)
    {
        using var db = TestDb.Create();
        var def = Template(db, "Default", isDefault: true);
        db.TimetableBlocks.Add(new TimetableBlock { TemplateId = def.Id, Title = "Work", StartTime = new(9, 0), EndTime = new(17, 0) });
        var checklist = new Checklist { Name = "Daily" };
        checklist.Items.Add(new ChecklistItem { Title = "Water", Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily } });
        db.Checklists.Add(checklist);
        db.DayOverrides.Add(new DayOverride { Date = Monday, Mode = mode });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        Assert.Equal(Guid.Empty, plan.Template.Id);
        Assert.Empty(plan.Blocks);
        Assert.Equal(mode.ToString(), plan.Override!.Mode);
        Assert.Single(plan.Checklists);
    }

    // ------------------------------------------------------------------ checklist expansion

    [Fact]
    public async Task ChecklistItems_ExpandedForDate_ByRecurrenceAndDueDate()
    {
        using var db = TestDb.Create();
        var checklist = new Checklist { Name = "Main" };
        checklist.Items.Add(new ChecklistItem { Title = "Daily", Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily } });
        checklist.Items.Add(new ChecklistItem { Title = "Mondays", Recurrence = new RecurrenceRule { Type = RecurrenceType.Weekly, DaysOfWeek = [DayOfWeek.Monday] } });
        checklist.Items.Add(new ChecklistItem { Title = "Tuesdays", Recurrence = new RecurrenceRule { Type = RecurrenceType.Weekly, DaysOfWeek = [DayOfWeek.Tuesday] } });
        checklist.Items.Add(new ChecklistItem { Title = "One-off today", DueDate = Monday });
        checklist.Items.Add(new ChecklistItem { Title = "One-off tomorrow", DueDate = Monday.AddDays(1) });
        checklist.Items.Add(new ChecklistItem { Title = "Inactive", IsActive = false, Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily } });
        checklist.Items.Add(new ChecklistItem { Title = "Excepted", Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, ExceptionDates = [Monday] } });
        db.Checklists.Add(checklist);

        var archived = new Checklist { Name = "Archived", IsArchived = true };
        archived.Items.Add(new ChecklistItem { Title = "Archived daily", Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily } });
        db.Checklists.Add(archived);
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        var group = Assert.Single(plan.Checklists);
        Assert.Equal("Main", group.Name);
        Assert.Equal(
            new[] { "Daily", "Mondays", "One-off today" }.OrderBy(x => x),
            group.Items.Select(i => i.Title).OrderBy(x => x));
        Assert.Equal(3, group.TotalCount);
        Assert.Equal("Every day", group.Items.Single(i => i.Title == "Daily").RecurrenceLabel);
        Assert.Equal("Every Mon", group.Items.Single(i => i.Title == "Mondays").RecurrenceLabel);
        Assert.Equal("One-off", group.Items.Single(i => i.Title == "One-off today").RecurrenceLabel);
    }

    [Fact]
    public async Task ChecklistGroups_OrderedBySortOrder_ItemsByAnchorTimeThenSortOrder()
    {
        using var db = TestDb.Create();
        var second = new Checklist { Name = "Second", SortOrder = 2 };
        var first = new Checklist { Name = "First", SortOrder = 1 };
        var daily = new RecurrenceRule { Type = RecurrenceType.Daily };
        first.Items.Add(new ChecklistItem { Title = "Anytime", SortOrder = 0, Recurrence = daily });
        first.Items.Add(new ChecklistItem { Title = "At 08:00", SortOrder = 5, AnchorType = AnchorType.FixedTime, AnchorTime = new(8, 0), Recurrence = daily });
        first.Items.Add(new ChecklistItem { Title = "At 07:00", SortOrder = 9, AnchorType = AnchorType.FixedTime, AnchorTime = new(7, 0), Recurrence = daily });
        second.Items.Add(new ChecklistItem { Title = "X", Recurrence = daily });
        db.Checklists.AddRange(second, first);
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(6, 0)).BuildAsync(Monday);

        Assert.Equal(new[] { "First", "Second" }, plan.Checklists.Select(c => c.Name));
        Assert.Equal(new[] { "At 07:00", "At 08:00", "Anytime" }, plan.Checklists[0].Items.Select(i => i.Title));
    }

    // ------------------------------------------------------------------ completions

    [Fact]
    public async Task Completions_SetFlagsAndSummary_OnlyForThatDate_AndOnlyWhenDone()
    {
        using var db = TestDb.Create();
        var checklist = new Checklist { Name = "Main" };
        var daily = new RecurrenceRule { Type = RecurrenceType.Daily };
        var done = new ChecklistItem { Title = "Done", Recurrence = daily };
        var skipped = new ChecklistItem { Title = "Skipped", Recurrence = daily };
        var doneYesterday = new ChecklistItem { Title = "Done yesterday", Recurrence = daily };
        var open = new ChecklistItem { Title = "Open", Recurrence = daily };
        foreach (var i in new[] { done, skipped, doneYesterday, open }) checklist.Items.Add(i);
        db.Checklists.Add(checklist);
        var completedAt = new DateTimeOffset(2026, 10, 5, 3, 0, 0, TimeSpan.Zero);
        db.ChecklistCompletions.AddRange(
            new ChecklistCompletion { ChecklistItemId = done.Id, OccurrenceDate = Monday, Status = CompletionStatus.Done, CompletedAt = completedAt },
            new ChecklistCompletion { ChecklistItemId = skipped.Id, OccurrenceDate = Monday, Status = CompletionStatus.Skipped },
            new ChecklistCompletion { ChecklistItemId = doneYesterday.Id, OccurrenceDate = Monday.AddDays(-1), Status = CompletionStatus.Done });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        var items = plan.Checklists.Single().Items.ToDictionary(i => i.Title);
        Assert.True(items["Done"].IsCompleted);
        Assert.Equal(completedAt, items["Done"].CompletedAt);
        Assert.False(items["Skipped"].IsCompleted);
        Assert.False(items["Done yesterday"].IsCompleted);
        Assert.Null(items["Done yesterday"].CompletedAt);
        Assert.False(items["Open"].IsCompleted);

        Assert.Equal(1, plan.Checklists.Single().CompletedCount);
        Assert.Equal(4, plan.Summary.TotalItems);
        Assert.Equal(1, plan.Summary.CompletedItems);
        Assert.Equal(25, plan.Summary.CompletionPercent);
    }

    [Fact]
    public async Task LinkedItems_AttachToTheirBlock_WithCompletionFlag()
    {
        using var db = TestDb.Create();
        var def = Template(db, "Default", isDefault: true);
        var block = new TimetableBlock { TemplateId = def.Id, Title = "Gym", StartTime = new(18, 0), EndTime = new(19, 0) };
        db.TimetableBlocks.Add(block);
        var checklist = new Checklist { Name = "Fitness" };
        var item = new ChecklistItem
        {
            Title = "Stretch", AnchorType = AnchorType.LinkedToBlock, TimetableBlockId = block.Id,
            Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily }
        };
        checklist.Items.Add(item);
        db.Checklists.Add(checklist);
        db.ChecklistCompletions.Add(new ChecklistCompletion { ChecklistItemId = item.Id, OccurrenceDate = Monday });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        var linked = Assert.Single(Assert.Single(plan.Blocks).LinkedItems);
        Assert.Equal(item.Id, linked.ItemId);
        Assert.True(linked.IsCompleted);
    }

    // ------------------------------------------------------------------ future tasks

    [Fact]
    public async Task FutureTasks_PendingDueOnDate_AppearInDueToday_EarlierPendingInOverdue()
    {
        using var db = TestDb.Create();
        db.FutureTasks.AddRange(
            new FutureTask { Title = "Due today", DueDate = Monday, DueTime = new(15, 0), Priority = Priority.High },
            new FutureTask { Title = "Done today", DueDate = Monday, Status = FutureTaskStatus.Done },
            new FutureTask { Title = "Cancelled today", DueDate = Monday, Status = FutureTaskStatus.Cancelled },
            new FutureTask { Title = "Tomorrow", DueDate = Monday.AddDays(1) },
            new FutureTask { Title = "Late", DueDate = Monday.AddDays(-2) },
            new FutureTask { Title = "Late but done", DueDate = Monday.AddDays(-2), Status = FutureTaskStatus.Done });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday);

        var due = Assert.Single(plan.DueToday);
        Assert.Equal("Due today", due.Title);
        Assert.Equal(new TimeOnly(15, 0), due.DueTime);
        Assert.Equal("High", due.Priority);
        Assert.Equal("Late", Assert.Single(plan.Overdue).Title);
    }

    [Fact]
    public async Task PastDate_HasNoOverdueSection()
    {
        using var db = TestDb.Create();
        db.FutureTasks.Add(new FutureTask { Title = "Ancient", DueDate = Monday.AddDays(-10) });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 0)).BuildAsync(Monday.AddDays(-1));

        Assert.Empty(plan.Overdue);
    }

    // ------------------------------------------------------------------ today vs other dates

    private static async Task<(AppDbContext Db, ChecklistItem FixedItem)> SeedDayAsync()
    {
        var db = TestDb.Create();
        var def = Template(db, "Default", isDefault: true);
        db.TimetableBlocks.AddRange(
            new TimetableBlock { TemplateId = def.Id, Title = "Early", StartTime = new(7, 0), EndTime = new(8, 0) },
            new TimetableBlock { TemplateId = def.Id, Title = "Work", StartTime = new(9, 0), EndTime = new(11, 0), Category = BlockCategory.Work },
            new TimetableBlock { TemplateId = def.Id, Title = "Lunch", StartTime = new(12, 0), EndTime = new(13, 0) });
        var checklist = new Checklist { Name = "Main" };
        var fixedItem = new ChecklistItem
        {
            Title = "Pills", AnchorType = AnchorType.FixedTime, AnchorTime = new(9, 30),
            Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily }
        };
        checklist.Items.Add(fixedItem);
        db.Checklists.Add(checklist);
        await db.SaveChangesAsync();
        return (db, fixedItem);
    }

    [Fact]
    public async Task Today_ComputesBlockStates_NowAndNextBlocks_AndOverdueItems()
    {
        var (db, _) = await SeedDayAsync();
        using var _db = db;

        var plan = await Builder(db, Monday, new TimeOnly(10, 30)).BuildAsync(Monday);

        Assert.Equal(new[] { "past", "current", "upcoming" }, plan.Blocks.Select(b => b.State));
        Assert.NotNull(plan.NowBlock);
        Assert.Equal("Work", plan.NowBlock!.Title);
        Assert.Equal("Work", plan.NowBlock.Category);
        Assert.Equal(90, plan.NowBlock.ElapsedMinutes);
        Assert.Equal(30, plan.NowBlock.RemainingMinutes);
        Assert.Equal(75, plan.NowBlock.ProgressPercent);
        Assert.NotNull(plan.NextBlock);
        Assert.Equal("Lunch", plan.NextBlock!.Title);
        Assert.Equal(90, plan.NextBlock.StartsInMinutes);
        Assert.True(plan.Checklists.Single().Items.Single().IsOverdue);
        Assert.Equal(1, plan.Summary.BlocksDone);
        Assert.Equal(3, plan.Summary.BlocksTotal);
        Assert.Equal(240, plan.Summary.MinutesScheduled);
    }

    [Fact]
    public async Task Today_CompletedFixedTimeItem_IsNotOverdue()
    {
        var (db, item) = await SeedDayAsync();
        using var _db = db;
        db.ChecklistCompletions.Add(new ChecklistCompletion { ChecklistItemId = item.Id, OccurrenceDate = Monday });
        await db.SaveChangesAsync();

        var plan = await Builder(db, Monday, new TimeOnly(10, 30)).BuildAsync(Monday);

        Assert.False(plan.Checklists.Single().Items.Single().IsOverdue);
    }

    [Fact]
    public async Task FutureDate_AllBlocksUpcoming_NoNowOrNextBlock_NothingOverdue()
    {
        var (db, _) = await SeedDayAsync();
        using var _db = db;

        var plan = await Builder(db, Monday, new TimeOnly(10, 30)).BuildAsync(Monday.AddDays(1));

        Assert.All(plan.Blocks, b => Assert.Equal("upcoming", b.State));
        Assert.Null(plan.NowBlock);
        Assert.Null(plan.NextBlock);
        Assert.False(plan.Checklists.Single().Items.Single().IsOverdue);
        Assert.Equal(0, plan.Summary.BlocksDone);
    }

    [Fact]
    public async Task PastDate_AllBlocksPast_NoNowOrNextBlock_IncompleteFixedItemsOverdue()
    {
        var (db, _) = await SeedDayAsync();
        using var _db = db;

        var plan = await Builder(db, Monday, new TimeOnly(6, 0)).BuildAsync(Monday.AddDays(-1));

        Assert.All(plan.Blocks, b => Assert.Equal("past", b.State));
        Assert.Null(plan.NowBlock);
        Assert.Null(plan.NextBlock);
        Assert.True(plan.Checklists.Single().Items.Single().IsOverdue);
        Assert.Equal(3, plan.Summary.BlocksDone);
    }

    [Fact]
    public async Task Today_IsDeterminedByClockInUserZone_NotTheMachineZone()
    {
        var (db, _) = await SeedDayAsync();
        using var _db = db;
        // 00:15 IST on Monday is still Sunday 18:45 UTC — Monday must be treated as today.
        var clock = new FixedAppClock(new DateTimeOffset(2026, 10, 4, 18, 45, 0, TimeSpan.Zero).ToOffset(FixedAppClock.IstOffset));

        var plan = await new DayPlanBuilder(db, clock).BuildAsync(Monday);

        Assert.All(plan.Blocks, b => Assert.Equal("upcoming", b.State));
        Assert.NotNull(plan.NextBlock);
        Assert.Equal("Early", plan.NextBlock!.Title);
        Assert.Equal(405, plan.NextBlock.StartsInMinutes);
    }
}
