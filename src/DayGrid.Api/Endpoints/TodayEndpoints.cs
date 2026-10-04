using DayGrid.Application.Scheduling;
using DayGrid.Application.Time;

namespace DayGrid.Api.Endpoints;

/// <summary>The money endpoint (plan section 5.1) plus its small supporting reads. Everything here
/// is a thin call into <see cref="IDayPlanBuilder"/> — no business logic lives in this file.</summary>
public static class TodayEndpoints
{
    private const int MaxRangeDays = 366;

    public static IEndpointRouteBuilder MapTodayEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Today");

        group.MapGet("/today", async (IDayPlanBuilder builder, IAppClock clock, DateOnly? date, CancellationToken ct) =>
        {
            var target = date ?? clock.Today;
            var plan = await builder.BuildAsync(target, ct);
            return Results.Ok(plan);
        })
        .WithName("GetToday")
        .Produces(StatusCodes.Status200OK);

        // Tiny poll fallback for the current-block card — same data as `today`, trimmed to just
        // the now/next pieces, for clients that don't want to refetch the whole payload every tick.
        group.MapGet("/today/now", async (IDayPlanBuilder builder, IAppClock clock, CancellationToken ct) =>
        {
            var plan = await builder.BuildAsync(clock.Today, ct);
            return Results.Ok(new { plan.NowBlock, plan.NextBlock });
        })
        .WithName("GetTodayNow");

        group.MapGet("/days/{date}/summary", async (IDayPlanBuilder builder, DateOnly date, CancellationToken ct) =>
        {
            var plan = await builder.BuildAsync(date, ct);
            return Results.Ok(plan.Summary);
        })
        .WithName("GetDaySummary");

        group.MapGet("/days/range", async (IDayPlanBuilder builder, DateOnly from, DateOnly to, CancellationToken ct) =>
        {
            if (to < from)
                return Results.BadRequest(new { error = "'to' must not be before 'from'." });
            if (to.DayNumber - from.DayNumber > MaxRangeDays)
                return Results.BadRequest(new { error = $"Range must not exceed {MaxRangeDays} days." });

            // A month range is at most ~31 iterations — building the full plan per day is wasteful
            // but simple and correct; revisit with a dedicated lightweight query if this becomes a
            // hot path (see plan section 8, "/today getting slow").
            var results = new List<object>();
            for (var d = from; d <= to; d = d.AddDays(1))
            {
                var plan = await builder.BuildAsync(d, ct);
                results.Add(new { date = d, plan.Summary });
            }

            return Results.Ok(results);
        })
        .WithName("GetDaysRange");

        return app;
    }
}
