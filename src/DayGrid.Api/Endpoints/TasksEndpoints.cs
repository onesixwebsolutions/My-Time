using DayGrid.Application.Dtos;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

/// <summary>
/// Plan section 5.5b — the standalone Tasks module. This file has ZERO references to
/// <c>IDayPlanBuilder</c>, <c>RecurrenceEngine</c>, or any <c>Reminder</c>/notification type,
/// on purpose (see DayGrid.Application/Services/README.md). It is the simplest endpoint file in
/// the app: bind, query/mutate <see cref="AppDbContext"/> directly, map to a DTO, return.
/// </summary>
public static class TasksEndpoints
{
    public static IEndpointRouteBuilder MapTasksEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tasks").WithTags("Tasks");

        group.MapGet("/", async (AppDbContext db, SimpleTaskStatus? status, string? q, string? sort, CancellationToken ct) =>
        {
            var query = db.SimpleTasks.AsNoTracking().AsQueryable();

            if (status is { } s)
                query = query.Where(t => t.Status == s);

            if (!string.IsNullOrWhiteSpace(q))
                query = query.Where(t => EF.Functions.ILike(t.Title, $"%{q}%"));

            query = sort switch
            {
                "title" => query.OrderBy(t => t.Title),
                "priority" => query.OrderByDescending(t => t.Priority),
                "created" => query.OrderByDescending(t => t.CreatedAt),
                _ => query.OrderBy(t => t.SortOrder)
            };

            var tasks = await query.ToListAsync(ct);
            return Results.Ok(tasks.Select(ToDto));
        })
        .WithName("ListTasks");

        group.MapPost("/", async (AppDbContext db, CreateSimpleTaskRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

            var maxSortOrder = await db.SimpleTasks.MaxAsync(t => (int?)t.SortOrder, ct) ?? -1;

            var task = new SimpleTask
            {
                Title = request.Title.Trim(),
                Notes = request.Notes,
                Priority = request.Priority ?? Priority.Normal,
                SortOrder = maxSortOrder + 1
            };

            db.SimpleTasks.Add(task);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/v1/tasks/{task.Id}", ToDto(task));
        })
        .WithName("CreateTask");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, UpdateSimpleTaskRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });

            var task = await db.SimpleTasks.FindAsync([id], ct);
            if (task is null)
                return Results.NotFound();

            task.Title = request.Title.Trim();
            task.Notes = request.Notes;
            task.Priority = request.Priority;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(task));
        })
        .WithName("UpdateTask");

        group.MapPatch("/{id:guid}/status", async (AppDbContext db, Guid id, UpdateSimpleTaskStatusRequest request, CancellationToken ct) =>
        {
            var task = await db.SimpleTasks.FindAsync([id], ct);
            if (task is null)
                return Results.NotFound();

            task.Status = request.Status;
            task.CompletedAt = request.Status == SimpleTaskStatus.Done ? DateTimeOffset.UtcNow : null;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(ToDto(task));
        })
        .WithName("SetTaskStatus");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var task = await db.SimpleTasks.FindAsync([id], ct);
            if (task is null)
                return Results.NotFound();

            db.SimpleTasks.Remove(task);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteTask");

        group.MapPut("/reorder", async (AppDbContext db, ReorderRequest request, CancellationToken ct) =>
        {
            if (request.Items is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["Items are required."] });

            // Last entry wins on duplicate ids (ToDictionary threw -> 500).
            var sortOrderById = new Dictionary<Guid, int>();
            foreach (var item in request.Items.Where(i => i is not null))
                sortOrderById[item.Id] = item.SortOrder;
            var ids = sortOrderById.Keys.ToList();
            var tasks = await db.SimpleTasks.Where(t => ids.Contains(t.Id)).ToListAsync(ct);

            foreach (var task in tasks)
            {
                task.SortOrder = sortOrderById[task.Id];
                task.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("ReorderTasks");

        return app;
    }

    private static SimpleTaskDto ToDto(SimpleTask t) => new()
    {
        Id = t.Id,
        Title = t.Title,
        Notes = t.Notes,
        Priority = t.Priority,
        Status = t.Status,
        SortOrder = t.SortOrder,
        CompletedAt = t.CompletedAt,
        CreatedAt = t.CreatedAt,
        UpdatedAt = t.UpdatedAt
    };
}
