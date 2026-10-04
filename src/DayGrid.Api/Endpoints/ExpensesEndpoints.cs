using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

// Request bodies local to this endpoint group — both entities are flat (no navigation
// properties, no relationships to any other table), so there's no cycle-risk in returning them
// directly from Results.Ok, unlike FutureTask/Reminder.
public record ConstantExpenseRequest(string Name, decimal Amount, string? Category, int? DayOfMonth, string? Notes);
public record VaryingExpenseRequest(string Title, decimal Amount, string? Category, DateOnly Date, string? Notes);
public record CompletedSpendRequest(string Title, decimal Amount, string? Category, DateOnly Date, string? Notes);

/// <summary>
/// Expenses module — monthly constant expenses (rent, subscriptions), varying/one-off expenses,
/// and "My Spends" (a running log of money actually spent this month, entered as you go).
/// </summary>
public static class ExpensesEndpoints
{
    public static IEndpointRouteBuilder MapExpensesEndpoints(this IEndpointRouteBuilder app)
    {
        MapConstantExpenses(app);
        MapVaryingExpenses(app);
        MapCompletedSpends(app);
        return app;
    }

    private static void MapConstantExpenses(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/expenses/constant").WithTags("Expenses");

        group.MapGet("/", async (AppDbContext db, bool includeInactive = false, CancellationToken ct = default) =>
        {
            var query = db.ConstantExpenses.AsNoTracking().AsQueryable();
            if (!includeInactive)
                query = query.Where(x => x.IsActive);

            var items = await query.OrderBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListConstantExpenses");

        group.MapGet("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var item = await db.ConstantExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithName("GetConstantExpense");

        group.MapPost("/", async (AppDbContext db, ConstantExpenseRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });
            if (request.DayOfMonth is < 1 or > 31)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dayOfMonth"] = ["Day of month must be between 1 and 31."] });

            var expense = new ConstantExpense
            {
                Name = request.Name.Trim(),
                Amount = request.Amount,
                Category = request.Category?.Trim(),
                DayOfMonth = request.DayOfMonth,
                Notes = request.Notes
            };
            db.ConstantExpenses.Add(expense);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/expenses/constant/{expense.Id}", expense);
        })
        .WithName("CreateConstantExpense");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, ConstantExpenseRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Name))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["Name is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });
            if (request.DayOfMonth is < 1 or > 31)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["dayOfMonth"] = ["Day of month must be between 1 and 31."] });

            var expense = await db.ConstantExpenses.FindAsync([id], ct);
            if (expense is null) return Results.NotFound();

            expense.Name = request.Name.Trim();
            expense.Amount = request.Amount;
            expense.Category = request.Category?.Trim();
            expense.DayOfMonth = request.DayOfMonth;
            expense.Notes = request.Notes;
            expense.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(expense);
        })
        .WithName("UpdateConstantExpense");

        group.MapPatch("/{id:guid}/active", async (AppDbContext db, Guid id, bool active, CancellationToken ct) =>
        {
            var expense = await db.ConstantExpenses.FindAsync([id], ct);
            if (expense is null) return Results.NotFound();

            expense.IsActive = active;
            expense.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(expense);
        })
        .WithName("SetConstantExpenseActive");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var expense = await db.ConstantExpenses.FindAsync([id], ct);
            if (expense is null) return Results.NotFound();

            db.ConstantExpenses.Remove(expense);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteConstantExpense");
    }

    private static void MapVaryingExpenses(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/expenses/varying").WithTags("Expenses");

        group.MapGet("/", async (AppDbContext db, DateOnly? from, DateOnly? to, string? category, CancellationToken ct) =>
        {
            var query = db.VaryingExpenses.AsNoTracking().AsQueryable();
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } toDate) query = query.Where(x => x.Date <= toDate);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);

            var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListVaryingExpenses");

        group.MapGet("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var item = await db.VaryingExpenses.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithName("GetVaryingExpense");

        group.MapPost("/", async (AppDbContext db, VaryingExpenseRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });

            var expense = new VaryingExpense
            {
                Title = request.Title.Trim(),
                Amount = request.Amount,
                Category = request.Category?.Trim(),
                Date = request.Date,
                Notes = request.Notes
            };
            db.VaryingExpenses.Add(expense);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/expenses/varying/{expense.Id}", expense);
        })
        .WithName("CreateVaryingExpense");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, VaryingExpenseRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });

            var expense = await db.VaryingExpenses.FindAsync([id], ct);
            if (expense is null) return Results.NotFound();

            expense.Title = request.Title.Trim();
            expense.Amount = request.Amount;
            expense.Category = request.Category?.Trim();
            expense.Date = request.Date;
            expense.Notes = request.Notes;
            expense.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(expense);
        })
        .WithName("UpdateVaryingExpense");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var expense = await db.VaryingExpenses.FindAsync([id], ct);
            if (expense is null) return Results.NotFound();

            db.VaryingExpenses.Remove(expense);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteVaryingExpense");
    }

    private static void MapCompletedSpends(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/expenses/spends").WithTags("Expenses");

        group.MapGet("/", async (AppDbContext db, DateOnly? from, DateOnly? to, string? category, CancellationToken ct) =>
        {
            var query = db.CompletedSpends.AsNoTracking().AsQueryable();
            if (from is { } f) query = query.Where(x => x.Date >= f);
            if (to is { } toDate) query = query.Where(x => x.Date <= toDate);
            if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);

            var items = await query.OrderByDescending(x => x.Date).ThenByDescending(x => x.CreatedAt).ToListAsync(ct);
            return Results.Ok(items);
        })
        .WithName("ListCompletedSpends");

        group.MapGet("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var item = await db.CompletedSpends.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithName("GetCompletedSpend");

        group.MapPost("/", async (AppDbContext db, CompletedSpendRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });

            var spend = new CompletedSpend
            {
                Title = request.Title.Trim(),
                Amount = request.Amount,
                Category = request.Category?.Trim(),
                Date = request.Date,
                Notes = request.Notes
            };
            db.CompletedSpends.Add(spend);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/expenses/spends/{spend.Id}", spend);
        })
        .WithName("CreateCompletedSpend");

        group.MapPut("/{id:guid}", async (AppDbContext db, Guid id, CompletedSpendRequest request, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
            if (request.Amount < 0)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amount"] = ["Amount cannot be negative."] });

            var spend = await db.CompletedSpends.FindAsync([id], ct);
            if (spend is null) return Results.NotFound();

            spend.Title = request.Title.Trim();
            spend.Amount = request.Amount;
            spend.Category = request.Category?.Trim();
            spend.Date = request.Date;
            spend.Notes = request.Notes;
            spend.UpdatedAt = DateTimeOffset.UtcNow;

            await db.SaveChangesAsync(ct);
            return Results.Ok(spend);
        })
        .WithName("UpdateCompletedSpend");

        group.MapDelete("/{id:guid}", async (AppDbContext db, Guid id, CancellationToken ct) =>
        {
            var spend = await db.CompletedSpends.FindAsync([id], ct);
            if (spend is null) return Results.NotFound();

            db.CompletedSpends.Remove(spend);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        })
        .WithName("DeleteCompletedSpend");
    }
}
