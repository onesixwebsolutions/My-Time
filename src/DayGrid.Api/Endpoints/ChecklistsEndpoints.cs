using DayGrid.Application.Dtos;
using DayGrid.Application.Scheduling;
using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

/// <summary>Checklists, checklist items, completions and the recurrence preview (plan sections 5.2/5.3).</summary>
public static class ChecklistsEndpoints
{
    public static IEndpointRouteBuilder MapChecklistEndpoints(this IEndpointRouteBuilder app)
    {
        MapChecklistCrud(app);
        MapItemCrud(app);
        MapCompletions(app);
        MapPreview(app);
        return app;
    }

    private static void MapChecklistCrud(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/checklists").WithTags("Checklists");

        group.MapGet("/", async (AppDbContext db, IAppClock clock, bool includeArchived = false, CancellationToken ct = default) =>
        {
            var today = clock.Today;
            var query = db.Checklists.AsNoTracking().Include(c => c.Items).AsQueryable();
            if (!includeArchived)
                query = query.Where(c => !c.IsArchived);

            var checklists = await query.OrderBy(c => c.SortOrder).ToListAsync(ct);
            var completions = await db.ChecklistCompletions.AsNoTracking()
                .Where(c => c.OccurrenceDate == today)
                .ToListAsync(ct);
            var completedIds = completions.Select(c => c.ChecklistItemId).ToHashSet();

            return Results.Ok(checklists.Select(c => ToDto(c, completedIds)));
        })
        .WithName("ListChecklists");

        group.MapGet("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var checklist = await db.Checklists.AsNoTracking()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == id, ct);
            if (checklist is null)
                return Results.NotFound();

            var dto = ToDto(checklist, null);
            var items = checklist.Items.OrderBy(i => i.SortOrder).Select(ToItemDto);
            return Results.Ok(new
            {
                dto.Id,
                dto.Name,
                dto.Description,
                dto.Color,
                dto.Icon,
                dto.SortOrder,
                dto.IsArchived,
                dto.ItemCount,
                dto.CompletedTodayCount,
                dto.CreatedAt,
                dto.UpdatedAt,
                Items = items
            });
        })
        .WithName("GetChecklist");

        group.MapPost("/", async (AppDbContext db, CreateChecklistRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Required("name", "Name is required.");

            var checklist = new Checklist
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                Color = request.Color,
                Icon = request.Icon,
                SortOrder = request.SortOrder
            };
            db.Checklists.Add(checklist);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/checklists/{checklist.Id}", ToDto(checklist, null));
        })
        .WithName("CreateChecklist");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, UpdateChecklistRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Required("name", "Name is required.");

            var checklist = await db.Checklists.FindAsync([id], ct);
            if (checklist is null)
                return Results.NotFound();

            checklist.Name = request.Name.Trim();
            checklist.Description = request.Description;
            checklist.Color = request.Color;
            checklist.Icon = request.Icon;
            checklist.SortOrder = request.SortOrder;
            checklist.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(checklist, null));
        })
        .WithName("UpdateChecklist");

        group.MapPatch("/{id:guid}/archive", async (AppDbContext db, Guid id, bool archived, CancellationToken ct) =>
        {
            var checklist = await db.Checklists.FindAsync([id], ct);
            if (checklist is null)
                return Results.NotFound();

            checklist.IsArchived = archived;
            checklist.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(checklist, null));
        })
        .WithName("ArchiveChecklist");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var checklist = await db.Checklists.FindAsync([id], ct);
            if (checklist is null)
                return Results.NotFound();

            db.Checklists.Remove(checklist); // cascades to items -> completions per configuration
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteChecklist");

        group.MapPut("/reorder", async (AppDbContext db, ReorderRequest request, CancellationToken ct) =>
        {
            if (ToSortOrderMap(request) is not { } sortOrderById)
                return Required("items", "Items are required.");
            var ids = sortOrderById.Keys.ToList();
            var checklists = await db.Checklists.Where(c => ids.Contains(c.Id)).ToListAsync(ct);

            foreach (var checklist in checklists)
            {
                checklist.SortOrder = sortOrderById[checklist.Id];
                checklist.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderChecklists");

        group.MapGet("/{id:guid}/items", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var items = await db.ChecklistItems.AsNoTracking()
                .Where(i => i.ChecklistId == id)
                .OrderBy(i => i.SortOrder)
                .ToListAsync(ct);
            return Results.Ok(items.Select(ToItemDto));
        })
        .WithName("ListChecklistItems");

        group.MapPost("/{id:guid}/items", async (AppDbContext db, Guid id, CreateChecklistItemRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Required("title", "Title is required.");

            var checklistExists = await db.Checklists.AnyAsync(c => c.Id == id, ct);
            if (!checklistExists)
                return Results.NotFound(new { error = "Checklist not found." });

            if (request.TimetableBlockId is { } newBlockId && !await db.TimetableBlocks.AnyAsync(b => b.Id == newBlockId, ct))
                return Required("timetableBlockId", "Timetable block not found.");

            var maxSortOrder = await db.ChecklistItems.Where(i => i.ChecklistId == id).MaxAsync(i => (int?)i.SortOrder, ct) ?? -1;

            var item = new ChecklistItem
            {
                ChecklistId = id,
                Title = request.Title.Trim(),
                Notes = request.Notes,
                Priority = request.Priority,
                EstimatedMinutes = request.EstimatedMinutes,
                AnchorType = request.AnchorType,
                AnchorTime = request.AnchorTime,
                WindowStart = request.WindowStart,
                WindowEnd = request.WindowEnd,
                TimetableBlockId = request.TimetableBlockId,
                Recurrence = request.Recurrence ?? new(),
                DueDate = request.DueDate,
                ReminderOffsetMinutes = request.ReminderOffsetMinutes,
                SortOrder = maxSortOrder + 1
            };

            db.ChecklistItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/items/{item.Id}", ToItemDto(item));
        })
        .WithName("CreateChecklistItem");

        group.MapPut("/{id:guid}/items/reorder", async (AppDbContext db, Guid id, ReorderRequest request, CancellationToken ct) =>
        {
            if (ToSortOrderMap(request) is not { } sortOrderById)
                return Required("items", "Items are required.");
            var ids = sortOrderById.Keys.ToList();
            var items = await db.ChecklistItems.Where(i => i.ChecklistId == id && ids.Contains(i.Id)).ToListAsync(ct);

            foreach (var item in items)
            {
                item.SortOrder = sortOrderById[item.Id];
                item.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderChecklistItems");
    }

    private static void MapItemCrud(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items").WithTags("Checklist Items");

        group.MapPut("/{itemId:guid}", async (AppDbContext db, Guid itemId, UpdateChecklistItemRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Required("title", "Title is required.");

            var item = await db.ChecklistItems.FindAsync([itemId], ct);
            if (item is null)
                return Results.NotFound();

            if (request.TimetableBlockId is { } newBlockId && !await db.TimetableBlocks.AnyAsync(b => b.Id == newBlockId, ct))
                return Required("timetableBlockId", "Timetable block not found.");

            item.Title = request.Title.Trim();
            item.Notes = request.Notes;
            item.Priority = request.Priority;
            item.EstimatedMinutes = request.EstimatedMinutes;
            item.AnchorType = request.AnchorType;
            item.AnchorTime = request.AnchorTime;
            item.WindowStart = request.WindowStart;
            item.WindowEnd = request.WindowEnd;
            item.TimetableBlockId = request.TimetableBlockId;
            item.Recurrence = request.Recurrence ?? new();
            item.DueDate = request.DueDate;
            item.ReminderOffsetMinutes = request.ReminderOffsetMinutes;
            item.IsActive = request.IsActive;
            item.UpdatedAt = DateTimeOffset.UtcNow;

            // Note: editing a recurring item only changes the future — past ChecklistCompletion
            // rows are untouched, per plan section 8 ("editing a recurring item retroactively
            // rewrites history" mitigation).
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToItemDto(item));
        })
        .WithName("UpdateChecklistItem");

        group.MapDelete("/{itemId:guid}", async (AppDbContext db, Guid itemId, CancellationToken ct) =>
        {
            var item = await db.ChecklistItems.FindAsync([itemId], ct);
            if (item is null)
                return Results.NotFound();

            db.ChecklistItems.Remove(item);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteChecklistItem");

        group.MapPatch("/{itemId:guid}/active", async (AppDbContext db, Guid itemId, bool active, CancellationToken ct) =>
        {
            var item = await db.ChecklistItems.FindAsync([itemId], ct);
            if (item is null)
                return Results.NotFound();

            item.IsActive = active;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToItemDto(item));
        })
        .WithName("SetChecklistItemActive");

        group.MapGet("/{itemId:guid}/history", async (AppDbContext db, Guid itemId, DateOnly from, DateOnly to, CancellationToken ct) =>
        {
            var completions = await db.ChecklistCompletions.AsNoTracking()
                .Where(c => c.ChecklistItemId == itemId && c.OccurrenceDate >= from && c.OccurrenceDate <= to)
                .OrderByDescending(c => c.OccurrenceDate)
                .ToListAsync(ct);

            var history = completions.Select(c => new ChecklistItemHistoryEntryDto
            {
                OccurrenceDate = c.OccurrenceDate,
                Status = c.Status,
                CompletedAt = c.CompletedAt,
                Note = c.Note
            }).ToList();

            // Current streak: consecutive Done days walking back from the most recent occurrence.
            var streak = 0;
            foreach (var entry in history.OrderByDescending(h => h.OccurrenceDate))
            {
                if (entry.Status != Domain.Enums.CompletionStatus.Done)
                    break;
                streak++;
            }

            return Results.Ok(new { history, currentStreak = streak });
        })
        .WithName("GetChecklistItemHistory");
    }

    private static void MapCompletions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items").WithTags("Checklist Items");

        group.MapPost("/{itemId:guid}/complete", async (AppDbContext db, Guid itemId, CompleteItemRequest request, CancellationToken ct) =>
        {
            var itemExists = await db.ChecklistItems.AnyAsync(i => i.Id == itemId, ct);
            if (!itemExists)
                return Results.NotFound();

            var completion = await db.ChecklistCompletions
                .FirstOrDefaultAsync(c => c.ChecklistItemId == itemId && c.OccurrenceDate == request.Date, ct);

            if (completion is null)
            {
                completion = new ChecklistCompletion
                {
                    ChecklistItemId = itemId,
                    OccurrenceDate = request.Date
                };
                db.ChecklistCompletions.Add(completion);
            }

            completion.Status = request.Status;
            completion.Note = request.Note;
            completion.CompletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { completion.Id, completion.ChecklistItemId, completion.OccurrenceDate, completion.Status, completion.CompletedAt, completion.Note });
        })
        .WithName("CompleteChecklistItem");

        group.MapDelete("/{itemId:guid}/complete", async (AppDbContext db, Guid itemId, DateOnly date, CancellationToken ct) =>
        {
            var completion = await db.ChecklistCompletions
                .FirstOrDefaultAsync(c => c.ChecklistItemId == itemId && c.OccurrenceDate == date, ct);
            if (completion is null)
                return Results.NoContent();

            db.ChecklistCompletions.Remove(completion);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("UncompleteChecklistItem");

        group.MapPost("/{itemId:guid}/skip", async (AppDbContext db, Guid itemId, SkipItemRequest request, CancellationToken ct) =>
        {
            var itemExists = await db.ChecklistItems.AnyAsync(i => i.Id == itemId, ct);
            if (!itemExists)
                return Results.NotFound();

            var completion = await db.ChecklistCompletions
                .FirstOrDefaultAsync(c => c.ChecklistItemId == itemId && c.OccurrenceDate == request.Date, ct);

            if (completion is null)
            {
                completion = new ChecklistCompletion { ChecklistItemId = itemId, OccurrenceDate = request.Date };
                db.ChecklistCompletions.Add(completion);
            }

            completion.Status = Domain.Enums.CompletionStatus.Skipped;
            completion.Note = request.Reason;
            completion.CompletedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new { completion.Id, completion.ChecklistItemId, completion.OccurrenceDate, completion.Status, completion.CompletedAt, completion.Note });
        })
        .WithName("SkipChecklistItem");
    }

    private static void MapPreview(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/items/preview-occurrences", (PreviewOccurrencesRequest request) =>
        {
            if (request.Recurrence is null)
                return Required("recurrence", "Recurrence is required.");
            if (request.To.DayNumber - request.From.DayNumber > MaxPreviewDays)
                return Required("to", $"Preview range must not exceed {MaxPreviewDays} days.");

            var dates = RecurrenceEngine.Expand(request.Recurrence, request.From, request.To).ToList();
            return Results.Ok(dates);
        })
        .WithTags("Checklist Items")
        .WithName("PreviewOccurrences");
    }

    private const int MaxPreviewDays = 3660;

    private static IResult Required(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    // Null when the body has no items list; duplicate ids resolve to the last entry instead of
    // throwing (ToDictionary on duplicates used to surface as a 500).
    private static Dictionary<Guid, int>? ToSortOrderMap(ReorderRequest request)
    {
        if (request.Items is null)
            return null;
        var map = new Dictionary<Guid, int>();
        foreach (var item in request.Items)
        {
            if (item is not null)
                map[item.Id] = item.SortOrder;
        }
        return map;
    }

    private static ChecklistDto ToDto(Checklist checklist, HashSet<Guid>? completedTodayItemIds)
    {
        var completedToday = completedTodayItemIds is null
            ? 0
            : checklist.Items.Count(i => completedTodayItemIds.Contains(i.Id));

        return new ChecklistDto
        {
            Id = checklist.Id,
            Name = checklist.Name,
            Description = checklist.Description,
            Color = checklist.Color,
            Icon = checklist.Icon,
            SortOrder = checklist.SortOrder,
            IsArchived = checklist.IsArchived,
            ItemCount = checklist.Items.Count,
            CompletedTodayCount = completedToday,
            CreatedAt = checklist.CreatedAt,
            UpdatedAt = checklist.UpdatedAt
        };
    }

    private static object ToItemDto(ChecklistItem item) => new
    {
        item.Id,
        item.ChecklistId,
        item.Title,
        item.Notes,
        item.Priority,
        item.EstimatedMinutes,
        item.AnchorType,
        item.AnchorTime,
        item.WindowStart,
        item.WindowEnd,
        item.TimetableBlockId,
        item.Recurrence,
        item.DueDate,
        item.ReminderOffsetMinutes,
        item.IsActive,
        item.SortOrder,
        item.CreatedAt,
        item.UpdatedAt
    };
}
