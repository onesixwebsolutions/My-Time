using DayGrid.Application.Dtos;
using DayGrid.Application.Scheduling;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Infrastructure.Scheduling;

/// <summary>
/// Concrete implementation of <see cref="IDayPlanBuilder"/> (interface defined in
/// DayGrid.Application — see the comment there for why). Resolves the winning template for a
/// date using the plan's most-specific-wins order, expands recurring checklist items with
/// <see cref="RecurrenceEngine"/>, joins completions, and assembles the single merged
/// <see cref="TodayDto"/> the home page renders in one call.
/// </summary>
public class DayPlanBuilder : IDayPlanBuilder
{
    private readonly AppDbContext _db;
    private readonly IAppClock _clock;

    public DayPlanBuilder(AppDbContext db, IAppClock clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<TodayDto> BuildAsync(DateOnly date, CancellationToken ct = default)
    {
        // One read of the clock so "today" and "now" can't straddle midnight between them.
        var now = _clock.Now;
        var today = DateOnly.FromDateTime(now.DateTime);
        var nowTime = TimeOnly.FromDateTime(now.DateTime);
        var isToday = date == today;

        var dayOverride = await _db.DayOverrides.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Date == date, ct);

        var template = await ResolveTemplateAsync(date, dayOverride, ct);

        var blocks = template is null
            ? new List<TimetableBlock>()
            : await _db.TimetableBlocks.AsNoTracking()
                .Where(b => b.TemplateId == template.Id)
                .OrderBy(b => b.StartTime).ThenBy(b => b.SortOrder)
                .ToListAsync(ct);

        // ------------------------------------------------------------------
        // Checklist items that apply to this date (recurrence expanded in-memory — the
        // dataset is small enough at this app's scale that a DB-side expansion isn't worth it).
        // ------------------------------------------------------------------
        // Identity resolution matters here: without it, each ChecklistItem row gets its own
        // distinct Checklist instance even when several items share a checklist, which would
        // silently break the GroupBy(i => i.Checklist) below.
        var activeItems = await _db.ChecklistItems.AsNoTrackingWithIdentityResolution()
            .Include(i => i.Checklist)
            .Where(i => i.IsActive && i.Checklist != null && !i.Checklist.IsArchived)
            .ToListAsync(ct);

        var itemsForDate = activeItems
            .Where(i => AppliesToDate(i, date))
            .ToList();

        var itemIds = itemsForDate.Select(i => i.Id).ToList();
        var completions = await _db.ChecklistCompletions.AsNoTracking()
            .Where(c => c.OccurrenceDate == date && itemIds.Contains(c.ChecklistItemId))
            .ToListAsync(ct);
        var completionByItemId = completions.ToDictionary(c => c.ChecklistItemId, c => c);

        var linkedItemsByBlockId = itemsForDate
            .Where(i => i.AnchorType == AnchorType.LinkedToBlock && i.TimetableBlockId.HasValue)
            .GroupBy(i => i.TimetableBlockId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        var blockDtos = blocks.Select(b => BuildBlockDto(b, isToday, nowTime, date, today, linkedItemsByBlockId, completionByItemId)).ToList();

        NowBlockDto? nowBlock = null;
        NextBlockDto? nextBlock = null;
        if (isToday)
        {
            var current = blocks.FirstOrDefault(b => nowTime >= b.StartTime && nowTime < b.EndTime);
            if (current is not null)
            {
                var elapsed = (int)(nowTime.ToTimeSpan() - current.StartTime.ToTimeSpan()).TotalMinutes;
                var total = (int)(current.EndTime.ToTimeSpan() - current.StartTime.ToTimeSpan()).TotalMinutes;
                nowBlock = new NowBlockDto
                {
                    BlockId = current.Id,
                    Title = current.Title,
                    StartTime = current.StartTime,
                    EndTime = current.EndTime,
                    Category = current.Category.ToString(),
                    Color = current.Color,
                    ElapsedMinutes = elapsed,
                    RemainingMinutes = Math.Max(total - elapsed, 0),
                    ProgressPercent = total > 0 ? Math.Clamp(elapsed * 100 / total, 0, 100) : 0
                };
            }

            var next = blocks.Where(b => b.StartTime > nowTime).OrderBy(b => b.StartTime).FirstOrDefault();
            if (next is not null)
            {
                var startsIn = (int)(next.StartTime.ToTimeSpan() - nowTime.ToTimeSpan()).TotalMinutes;
                nextBlock = new NextBlockDto
                {
                    BlockId = next.Id,
                    Title = next.Title,
                    StartTime = next.StartTime,
                    StartsInMinutes = Math.Max(startsIn, 0)
                };
            }
        }

        var checklistGroups = itemsForDate
            .GroupBy(i => i.Checklist!)
            .OrderBy(g => g.Key.SortOrder)
            .Select(g =>
            {
                var itemDtos = g
                    .OrderBy(i => i.AnchorTime ?? TimeOnly.MaxValue)
                    .ThenBy(i => i.SortOrder)
                    .Select(i => BuildItemDto(i, completionByItemId, isToday, nowTime, date, today))
                    .ToList();
                return new ChecklistGroupDto
                {
                    ChecklistId = g.Key.Id,
                    Name = g.Key.Name,
                    Color = g.Key.Color,
                    Icon = g.Key.Icon,
                    CompletedCount = itemDtos.Count(d => d.IsCompleted),
                    TotalCount = itemDtos.Count,
                    Items = itemDtos
                };
            })
            .ToList();

        var futureTasksForDate = await _db.FutureTasks.AsNoTracking()
            .Where(t => t.DueDate == date && t.Status == FutureTaskStatus.Pending)
            .ToListAsync(ct);
        var dueToday = futureTasksForDate.Select(ToDueTaskDto).ToList();

        var overdueTasks = date >= today
            ? await _db.FutureTasks.AsNoTracking()
                .Where(t => t.DueDate < date && t.Status == FutureTaskStatus.Pending)
                .ToListAsync(ct)
            : new List<FutureTask>();
        var overdue = overdueTasks.Select(ToDueTaskDto).ToList();

        var totalItems = checklistGroups.Sum(g => g.TotalCount);
        var completedItems = checklistGroups.Sum(g => g.CompletedCount);
        var blocksDone = blockDtos.Count(b => b.State == "past");
        var minutesScheduled = blocks.Sum(b => (int)(b.EndTime.ToTimeSpan() - b.StartTime.ToTimeSpan()).TotalMinutes);

        return new TodayDto
        {
            Date = date,
            DayOfWeek = date.DayOfWeek.ToString(),
            DisplayDate = date.ToDateTime(TimeOnly.MinValue).ToString("dddd, d MMMM yyyy"),
            Override = dayOverride is null ? null : new DayOverrideDto { Mode = dayOverride.Mode.ToString(), Note = dayOverride.Note },
            Template = new TemplateSummaryDto
            {
                Id = template?.Id ?? Guid.Empty,
                Name = template?.Name ?? "(none)",
                DayStart = template?.DayStart ?? new TimeOnly(0, 0),
                DayEnd = template?.DayEnd ?? new TimeOnly(23, 59),
                SlotMinutes = template?.SlotMinutes ?? 30
            },
            NowBlock = nowBlock,
            NextBlock = nextBlock,
            Blocks = blockDtos,
            Checklists = checklistGroups,
            DueToday = dueToday,
            Overdue = overdue,
            Summary = new DaySummaryDto
            {
                TotalItems = totalItems,
                CompletedItems = completedItems,
                CompletionPercent = totalItems > 0 ? completedItems * 100 / totalItems : 0,
                BlocksTotal = blockDtos.Count,
                BlocksDone = blocksDone,
                MinutesScheduled = minutesScheduled
            }
        };
    }

    private async Task<TimetableTemplate?> ResolveTemplateAsync(DateOnly date, DayOverride? dayOverride, CancellationToken ct)
    {
        if (dayOverride is not null)
        {
            if (dayOverride.Mode is DayOverrideMode.RestDay or DayOverrideMode.CustomOnly)
            {
                // RestDay hides everything; CustomOnly means "no template blocks, only ad-hoc
                // items" — this scaffold has no ad-hoc-item concept yet, so it behaves like RestDay
                // for the timetable rail (checklists/future tasks are unaffected either way).
                return null;
            }

            if (dayOverride.Mode == DayOverrideMode.UseTemplate && dayOverride.TemplateId is { } overrideTemplateId)
            {
                var overrideTemplate = await _db.TimetableTemplates.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.Id == overrideTemplateId, ct);
                if (overrideTemplate is not null)
                    return overrideTemplate;
                // Fall through to normal resolution if the override points at a deleted template.
            }
        }

        var specific = await _db.TimetableAssignments.AsNoTracking()
            .Where(a => a.Scope == AssignmentScope.SpecificDate && a.DateFrom == date)
            .OrderByDescending(a => a.Priority)
            .FirstOrDefaultAsync(ct);
        if (specific is not null)
            return await _db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == specific.TemplateId, ct);

        var range = await _db.TimetableAssignments.AsNoTracking()
            .Where(a => a.Scope == AssignmentScope.DateRange && a.DateFrom <= date && a.DateTo >= date)
            .OrderByDescending(a => a.Priority)
            .FirstOrDefaultAsync(ct);
        if (range is not null)
            return await _db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == range.TemplateId, ct);

        var weekday = await _db.TimetableAssignments.AsNoTracking()
            .Where(a => a.Scope == AssignmentScope.Weekday && a.DayOfWeek == date.DayOfWeek)
            .OrderByDescending(a => a.Priority)
            .FirstOrDefaultAsync(ct);
        if (weekday is not null)
            return await _db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == weekday.TemplateId, ct);

        return await _db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.IsDefault, ct);
    }

    private static bool AppliesToDate(ChecklistItem item, DateOnly date) =>
        item.Recurrence.Type == RecurrenceType.None
            ? item.DueDate == date
            : RecurrenceEngine.Occurs(item.Recurrence, date);

    private static TimetableBlockDto BuildBlockDto(
        TimetableBlock block,
        bool isToday,
        TimeOnly nowTime,
        DateOnly date,
        DateOnly today,
        IReadOnlyDictionary<Guid, List<ChecklistItem>> linkedItemsByBlockId,
        IReadOnlyDictionary<Guid, ChecklistCompletion> completionByItemId)
    {
        string state;
        if (date < today || (isToday && nowTime >= block.EndTime))
            state = "past";
        else if (isToday && nowTime >= block.StartTime && nowTime < block.EndTime)
            state = "current";
        else
            state = "upcoming";

        var linked = linkedItemsByBlockId.TryGetValue(block.Id, out var items)
            ? items.Select(i => new LinkedItemDto
            {
                ItemId = i.Id,
                Title = i.Title,
                IsCompleted = completionByItemId.TryGetValue(i.Id, out var c) && c.Status == CompletionStatus.Done
            }).ToList()
            : new List<LinkedItemDto>();

        return new TimetableBlockDto
        {
            Id = block.Id,
            Title = block.Title,
            StartTime = block.StartTime,
            EndTime = block.EndTime,
            Category = block.Category.ToString(),
            Color = block.Color,
            Location = block.Location,
            State = state,
            LinkedItems = linked
        };
    }

    private static ChecklistItemDto BuildItemDto(
        ChecklistItem item,
        IReadOnlyDictionary<Guid, ChecklistCompletion> completionByItemId,
        bool isToday,
        TimeOnly nowTime,
        DateOnly date,
        DateOnly today)
    {
        completionByItemId.TryGetValue(item.Id, out var completion);
        var isCompleted = completion is not null && completion.Status == CompletionStatus.Done;

        var isOverdue = !isCompleted
            && item.AnchorType == AnchorType.FixedTime
            && item.AnchorTime.HasValue
            && (date < today || (isToday && item.AnchorTime.Value < nowTime));

        return new ChecklistItemDto
        {
            ItemId = item.Id,
            Title = item.Title,
            AnchorType = item.AnchorType.ToString(),
            AnchorTime = item.AnchorTime,
            Priority = item.Priority.ToString(),
            EstimatedMinutes = item.EstimatedMinutes,
            IsCompleted = isCompleted,
            CompletedAt = completion?.CompletedAt,
            IsOverdue = isOverdue,
            RecurrenceLabel = DescribeRecurrence(item.Recurrence)
        };
    }

    private static DueTaskDto ToDueTaskDto(FutureTask task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        DueTime = task.DueTime,
        Priority = task.Priority.ToString()
    };

    /// <summary>Human-readable summary shown next to a checklist item, e.g. "Every day", "Weekdays".</summary>
    private static string DescribeRecurrence(RecurrenceRule rule)
    {
        switch (rule.Type)
        {
            case RecurrenceType.None:
                return "One-off";
            case RecurrenceType.Daily:
                return rule.Interval <= 1 ? "Every day" : $"Every {rule.Interval} days";
            case RecurrenceType.Weekly:
                var days = rule.DaysOfWeek.ToHashSet();
                var weekdays = new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday };
                var weekend = new HashSet<DayOfWeek> { DayOfWeek.Saturday, DayOfWeek.Sunday };
                if (days.SetEquals(weekdays)) return "Weekdays";
                if (days.SetEquals(weekend)) return "Weekends";
                return days.Count == 0 ? "Weekly" : "Every " + string.Join(", ", rule.DaysOfWeek.Select(d => d.ToString()[..3]));
            case RecurrenceType.MonthlyByDay:
                return $"Monthly on the {rule.DayOfMonth}";
            case RecurrenceType.MonthlyByWeekday:
                return rule.NthWeekday is { } nw ? $"Monthly, {Ordinal(nw.Nth)} {nw.DayOfWeek}" : "Monthly";
            case RecurrenceType.EveryNDays:
                return $"Every {rule.Interval} days";
            case RecurrenceType.Custom:
                return "Custom";
            default:
                return string.Empty;
        }
    }

    private static string Ordinal(int n)
    {
        if (n <= 0) return $"{n}th";
        return (n % 100) switch
        {
            11 or 12 or 13 => $"{n}th",
            _ => (n % 10) switch
            {
                1 => $"{n}st",
                2 => $"{n}nd",
                3 => $"{n}rd",
                _ => $"{n}th"
            }
        };
    }
}
