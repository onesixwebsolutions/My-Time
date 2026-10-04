using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

// Request bodies local to this endpoint group — kept here rather than in DayGrid.Application
// because they're pure transport shapes for a CRUD module (see Services/README.md).
public record CreateTemplateRequest(string Name, string? Description, TimeOnly DayStart, TimeOnly DayEnd, short SlotMinutes);
public record UpdateTemplateRequest(string Name, string? Description, TimeOnly DayStart, TimeOnly DayEnd, short SlotMinutes);
public record CreateBlockRequest(
    string Title, TimeOnly StartTime, TimeOnly EndTime, BlockCategory Category,
    string? Color, string? Location, Guid? ChecklistId, bool AllowOverlap, bool NotifyAtStart, int SortOrder);
public record UpdateBlockRequest(
    string Title, TimeOnly StartTime, TimeOnly EndTime, BlockCategory Category,
    string? Color, string? Location, Guid? ChecklistId, bool AllowOverlap, bool NotifyAtStart, int SortOrder);
public record MoveBlockRequest(TimeOnly StartTime, TimeOnly EndTime);
public record ValidateBlockRequest(Guid TemplateId, Guid? BlockId, TimeOnly StartTime, TimeOnly EndTime, bool AllowOverlap);
public record CreateAssignmentRequest(Guid TemplateId, AssignmentScope Scope, DayOfWeek? DayOfWeek, DateOnly? DateFrom, DateOnly? DateTo, int Priority);
public record UpsertDayOverrideRequest(DayOverrideMode Mode, Guid? TemplateId, string? Note);

public static class TimetableEndpoints
{
    public static IEndpointRouteBuilder MapTimetableEndpoints(this IEndpointRouteBuilder app)
    {
        MapTemplates(app);
        MapBlocks(app);
        MapAssignments(app);
        MapDayOverrides(app);
        return app;
    }

    private static void MapTemplates(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/timetable/templates").WithTags("Timetable");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.TimetableTemplates.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct)))
            .WithName("ListTemplates");

        group.MapGet("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var template = await db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (template is null) return Results.NotFound();

            var blocks = await db.TimetableBlocks.AsNoTracking()
                .Where(b => b.TemplateId == id)
                .OrderBy(b => b.StartTime)
                .ToListAsync(ct);

            return Results.Ok(new { template, blocks });
        })
        .WithName("GetTemplate");

        group.MapPost("/", async (AppDbContext db, CreateTemplateRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Invalid("name", "Name is required.");

            var template = new TimetableTemplate
            {
                Name = request.Name.Trim(),
                Description = request.Description,
                DayStart = request.DayStart,
                DayEnd = request.DayEnd,
                SlotMinutes = request.SlotMinutes
            };
            db.TimetableTemplates.Add(template);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/timetable/templates/{template.Id}", template);
        })
        .WithName("CreateTemplate");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, UpdateTemplateRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Invalid("name", "Name is required.");

            var template = await db.TimetableTemplates.FindAsync([id], ct);
            if (template is null) return Results.NotFound();

            template.Name = request.Name.Trim();
            template.Description = request.Description;
            template.DayStart = request.DayStart;
            template.DayEnd = request.DayEnd;
            template.SlotMinutes = request.SlotMinutes;
            template.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(template);
        })
        .WithName("UpdateTemplate");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var template = await db.TimetableTemplates.FindAsync([id], ct);
            if (template is null) return Results.NotFound();

            db.TimetableTemplates.Remove(template); // cascades to blocks + assignments
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteTemplate");

        group.MapPost("/{id:guid}/duplicate", async (AppDbContext db, Guid id, string? name, CancellationToken ct) =>
        {
            var source = await db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
            if (source is null) return Results.NotFound();

            var sourceBlocks = await db.TimetableBlocks.AsNoTracking().Where(b => b.TemplateId == id).ToListAsync(ct);

            var copy = new TimetableTemplate
            {
                Name = name ?? $"{source.Name} (copy)",
                Description = source.Description,
                DayStart = source.DayStart,
                DayEnd = source.DayEnd,
                SlotMinutes = source.SlotMinutes,
                IsDefault = false
            };
            db.TimetableTemplates.Add(copy);

            foreach (var block in sourceBlocks)
            {
                db.TimetableBlocks.Add(new TimetableBlock
                {
                    TemplateId = copy.Id,
                    Title = block.Title,
                    StartTime = block.StartTime,
                    EndTime = block.EndTime,
                    Category = block.Category,
                    Color = block.Color,
                    Location = block.Location,
                    ChecklistId = block.ChecklistId,
                    AllowOverlap = block.AllowOverlap,
                    NotifyAtStart = block.NotifyAtStart,
                    SortOrder = block.SortOrder
                });
            }

            await db.SaveChangesAsync(ct);
            // EF fixup filled copy.Blocks with blocks whose Template points back at copy — a cycle
            // System.Text.Json refuses to serialize (500). Drop the collection for the response.
            copy.Blocks = new List<TimetableBlock>();
            return Results.Created($"/api/v1/timetable/templates/{copy.Id}", copy);
        })
        .WithName("DuplicateTemplate");

        group.MapPatch("/{id:guid}/default", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var target = await db.TimetableTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
            if (target is null) return Results.NotFound();

            var current = await db.TimetableTemplates.Where(t => t.IsDefault).ToListAsync(ct);
            foreach (var t in current)
                t.IsDefault = false;

            target.IsDefault = true;
            await db.SaveChangesAsync(ct);
            return Results.Ok(target);
        })
        .WithName("SetDefaultTemplate");
    }

    private static void MapBlocks(IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/timetable/templates/{id:guid}/blocks", async (AppDbContext db, Guid id, CancellationToken ct) =>
            Results.Ok(await db.TimetableBlocks.AsNoTracking().Where(b => b.TemplateId == id).OrderBy(b => b.StartTime).ToListAsync(ct)))
            .WithTags("Timetable").WithName("ListBlocks");

        app.MapPost("/api/v1/timetable/templates/{id:guid}/blocks", async (AppDbContext db, Guid id, CreateBlockRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Invalid("title", "Title is required.");
            if (request.EndTime <= request.StartTime)
                return Results.BadRequest(new { error = "endTime must be after startTime." });
            if (!await db.TimetableTemplates.AnyAsync(t => t.Id == id, ct))
                return Results.NotFound();
            if (request.ChecklistId is { } checklistId && !await db.Checklists.AnyAsync(c => c.Id == checklistId, ct))
                return Invalid("checklistId", "Checklist not found.");

            if (!request.AllowOverlap)
            {
                var conflict = await FindOverlapAsync(db, id, null, request.StartTime, request.EndTime, ct);
                if (conflict is not null)
                    return Results.Conflict(new { error = "Overlaps an existing block.", conflictingBlockId = conflict.Id });
            }

            var block = new TimetableBlock
            {
                TemplateId = id,
                Title = request.Title.Trim(),
                StartTime = request.StartTime,
                EndTime = request.EndTime,
                Category = request.Category,
                Color = request.Color,
                Location = request.Location,
                ChecklistId = request.ChecklistId,
                AllowOverlap = request.AllowOverlap,
                NotifyAtStart = request.NotifyAtStart,
                SortOrder = request.SortOrder
            };
            db.TimetableBlocks.Add(block);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/timetable/blocks/{block.Id}", block);
        })
        .WithTags("Timetable").WithName("CreateBlock");

        var blockGroup = app.MapGroup("/api/v1/timetable/blocks").WithTags("Timetable");

        blockGroup.MapPut("/{blockId:guid}", async (AppDbContext db, Guid blockId, UpdateBlockRequest request, CancellationToken ct) =>
        {
            var block = await db.TimetableBlocks.FindAsync([blockId], ct);
            if (block is null) return Results.NotFound();

            if (string.IsNullOrWhiteSpace(request.Title))
                return Invalid("title", "Title is required.");
            if (request.EndTime <= request.StartTime)
                return Results.BadRequest(new { error = "endTime must be after startTime." });
            if (request.ChecklistId is { } checklistId && !await db.Checklists.AnyAsync(c => c.Id == checklistId, ct))
                return Invalid("checklistId", "Checklist not found.");

            if (!request.AllowOverlap)
            {
                var conflict = await FindOverlapAsync(db, block.TemplateId, blockId, request.StartTime, request.EndTime, ct);
                if (conflict is not null)
                    return Results.Conflict(new { error = "Overlaps an existing block.", conflictingBlockId = conflict.Id });
            }

            block.Title = request.Title.Trim();
            block.StartTime = request.StartTime;
            block.EndTime = request.EndTime;
            block.Category = request.Category;
            block.Color = request.Color;
            block.Location = request.Location;
            block.ChecklistId = request.ChecklistId;
            block.AllowOverlap = request.AllowOverlap;
            block.NotifyAtStart = request.NotifyAtStart;
            block.SortOrder = request.SortOrder;

            await db.SaveChangesAsync(ct);
            return Results.Ok(block);
        })
        .WithName("UpdateBlock");

        blockGroup.MapDelete("/{blockId:guid}", async (AppDbContext db, Guid blockId, CancellationToken ct) =>
        {
            var block = await db.TimetableBlocks.FindAsync([blockId], ct);
            if (block is null) return Results.NotFound();

            db.TimetableBlocks.Remove(block);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteBlock");

        blockGroup.MapPut("/{blockId:guid}/move", async (AppDbContext db, Guid blockId, MoveBlockRequest request, CancellationToken ct) =>
        {
            var block = await db.TimetableBlocks.FindAsync([blockId], ct);
            if (block is null) return Results.NotFound();

            if (request.EndTime <= request.StartTime)
                return Results.BadRequest(new { error = "endTime must be after startTime." });

            if (!block.AllowOverlap)
            {
                var conflict = await FindOverlapAsync(db, block.TemplateId, blockId, request.StartTime, request.EndTime, ct);
                if (conflict is not null)
                    return Results.Conflict(new { error = "Overlaps an existing block.", conflictingBlockId = conflict.Id });
            }

            block.StartTime = request.StartTime;
            block.EndTime = request.EndTime;
            await db.SaveChangesAsync(ct);
            return Results.Ok(block);
        })
        .WithName("MoveBlock");

        blockGroup.MapPost("/validate", async (AppDbContext db, ValidateBlockRequest request, CancellationToken ct) =>
        {
            if (request.AllowOverlap)
                return Results.Ok(new { valid = true, conflicts = Array.Empty<TimetableBlock>() });

            var conflicts = await db.TimetableBlocks.AsNoTracking()
                .Where(b => b.TemplateId == request.TemplateId
                    && (request.BlockId == null || b.Id != request.BlockId)
                    && b.StartTime < request.EndTime && request.StartTime < b.EndTime)
                .ToListAsync(ct);

            return Results.Ok(new { valid = conflicts.Count == 0, conflicts });
        })
        .WithName("ValidateBlock");
    }

    private static void MapAssignments(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/timetable/assignments").WithTags("Timetable");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.TimetableAssignments.AsNoTracking().ToListAsync(ct)))
            .WithName("ListAssignments");

        group.MapPost("/", async (AppDbContext db, CreateAssignmentRequest request, CancellationToken ct) =>
        {
            if (!await db.TimetableTemplates.AnyAsync(t => t.Id == request.TemplateId, ct))
                return Invalid("templateId", "Template not found.");

            var assignment = new TimetableAssignment
            {
                TemplateId = request.TemplateId,
                Scope = request.Scope,
                DayOfWeek = request.DayOfWeek,
                DateFrom = request.DateFrom,
                DateTo = request.DateTo,
                Priority = request.Priority
            };
            db.TimetableAssignments.Add(assignment);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/timetable/assignments/{assignment.Id}", assignment);
        })
        .WithName("CreateAssignment");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var assignment = await db.TimetableAssignments.FindAsync([id], ct);
            if (assignment is null) return Results.NotFound();

            db.TimetableAssignments.Remove(assignment);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteAssignment");

        app.MapGet("/api/v1/timetable/resolve", async (AppDbContext db, DateOnly date, CancellationToken ct) =>
        {
            var dayOverride = await db.DayOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.Date == date, ct);
            if (dayOverride is not null)
            {
                if (dayOverride.Mode is DayOverrideMode.RestDay or DayOverrideMode.CustomOnly)
                    return Results.Ok(new { templateId = (Guid?)null, reason = $"day_override:{dayOverride.Mode}" });

                // Same rule as DayPlanBuilder: an override pointing at a template that no longer
                // exists falls through to normal resolution instead of returning a dead id.
                if (dayOverride.TemplateId is { } overrideTemplateId
                    && await db.TimetableTemplates.AnyAsync(t => t.Id == overrideTemplateId, ct))
                    return Results.Ok(new { templateId = overrideTemplateId, reason = "day_override:UseTemplate" });
            }

            var specific = await db.TimetableAssignments.AsNoTracking()
                .Where(a => a.Scope == AssignmentScope.SpecificDate && a.DateFrom == date)
                .OrderByDescending(a => a.Priority).FirstOrDefaultAsync(ct);
            if (specific is not null)
                return Results.Ok(new { templateId = specific.TemplateId, reason = "assignment:SpecificDate" });

            var range = await db.TimetableAssignments.AsNoTracking()
                .Where(a => a.Scope == AssignmentScope.DateRange && a.DateFrom <= date && a.DateTo >= date)
                .OrderByDescending(a => a.Priority).FirstOrDefaultAsync(ct);
            if (range is not null)
                return Results.Ok(new { templateId = range.TemplateId, reason = "assignment:DateRange" });

            var weekday = await db.TimetableAssignments.AsNoTracking()
                .Where(a => a.Scope == AssignmentScope.Weekday && a.DayOfWeek == date.DayOfWeek)
                .OrderByDescending(a => a.Priority).FirstOrDefaultAsync(ct);
            if (weekday is not null)
                return Results.Ok(new { templateId = weekday.TemplateId, reason = "assignment:Weekday" });

            var defaultTemplate = await db.TimetableTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.IsDefault, ct);
            return Results.Ok(new { templateId = defaultTemplate?.Id, reason = defaultTemplate is null ? "none" : "template:IsDefault" });
        })
        .WithTags("Timetable").WithName("ResolveTemplateForDate");
    }

    private static void MapDayOverrides(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/day-overrides").WithTags("Timetable");

        group.MapGet("/", async (AppDbContext db, CancellationToken ct) =>
            Results.Ok(await db.DayOverrides.AsNoTracking().OrderBy(o => o.Date).ToListAsync(ct)))
            .WithName("ListDayOverrides");

        group.MapGet("/{date}", async (AppDbContext db, DateOnly date, CancellationToken ct) =>
        {
            var dayOverride = await db.DayOverrides.AsNoTracking().FirstOrDefaultAsync(o => o.Date == date, ct);
            return dayOverride is null ? Results.NotFound() : Results.Ok(dayOverride);
        })
        .WithName("GetDayOverride");

        group.MapPut("/{date}", async (AppDbContext db, DateOnly date, UpsertDayOverrideRequest request, CancellationToken ct) =>
        {
            if (request.TemplateId is { } templateId && !await db.TimetableTemplates.AnyAsync(t => t.Id == templateId, ct))
                return Invalid("templateId", "Template not found.");

            var dayOverride = await db.DayOverrides.FirstOrDefaultAsync(o => o.Date == date, ct);
            if (dayOverride is null)
            {
                dayOverride = new DayOverride { Date = date };
                db.DayOverrides.Add(dayOverride);
            }

            dayOverride.Mode = request.Mode;
            dayOverride.TemplateId = request.TemplateId;
            dayOverride.Note = request.Note;

            await db.SaveChangesAsync(ct);
            return Results.Ok(dayOverride);
        })
        .WithName("UpsertDayOverride");

        group.MapDelete("/{date}", async (AppDbContext db, DateOnly date, CancellationToken ct) =>
        {
            var dayOverride = await db.DayOverrides.FirstOrDefaultAsync(o => o.Date == date, ct);
            if (dayOverride is null) return Results.NoContent();

            db.DayOverrides.Remove(dayOverride);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteDayOverride");
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    private static Task<TimetableBlock?> FindOverlapAsync(AppDbContext db, Guid templateId, Guid? excludeBlockId, TimeOnly start, TimeOnly end, CancellationToken ct) =>
        db.TimetableBlocks.AsNoTracking()
            .Where(b => b.TemplateId == templateId
                && !b.AllowOverlap
                && (excludeBlockId == null || b.Id != excludeBlockId)
                && b.StartTime < end && start < b.EndTime)
            .FirstOrDefaultAsync(ct);
}
