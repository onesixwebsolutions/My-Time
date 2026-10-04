using DayGrid.Application.Time;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Api.Endpoints;

public record UpdateSettingsRequest(
    string TimeZone, DayOfWeek WeekStartsOn, TimeOnly DayStart, TimeOnly DayEnd,
    short DefaultSlotMinutes, bool EmailEnabled, string? EmailTo, TimeOnly? DailyDigestTime, string Theme);

public static class SettingsEndpoints
{
    public static IEndpointRouteBuilder MapSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").WithTags("Settings");

        // One row per user (tenant-filtered). Created at registration; recreated with defaults if missing.
        group.MapGet("/settings", async (AppDbContext db, IAppClockFactory clocks, CancellationToken ct) =>
            Results.Ok(await GetOrCreateSettingsAsync(db, clocks, ct)))
        .WithName("GetSettings");

        group.MapPut("/settings", async (AppDbContext db, IAppClockFactory clocks, UpdateSettingsRequest request, CancellationToken ct) =>
        {
            // time_zone and theme are NOT NULL columns — a null here used to surface as a 500 from the DB.
            if (string.IsNullOrWhiteSpace(request.TimeZone))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZone"] = ["Time zone is required."] });
            // It drives every "today"/"now" for this user, so it must be a real IANA zone.
            if (!clocks.IsValidTimeZone(request.TimeZone))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["timeZone"] = ["Time zone must be a valid IANA time zone id, e.g. 'Asia/Kolkata'."] });
            if (string.IsNullOrWhiteSpace(request.Theme))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["theme"] = ["Theme is required."] });

            var settings = await GetOrCreateSettingsAsync(db, clocks, ct, tracked: true);

            settings.TimeZone = request.TimeZone.Trim();
            settings.WeekStartsOn = request.WeekStartsOn;
            settings.DayStart = request.DayStart;
            settings.DayEnd = request.DayEnd;
            settings.DefaultSlotMinutes = request.DefaultSlotMinutes;
            settings.EmailEnabled = request.EmailEnabled;
            settings.EmailTo = request.EmailTo;
            settings.DailyDigestTime = request.DailyDigestTime;
            settings.Theme = request.Theme;

            await db.SaveChangesAsync(ct);
            return Results.Ok(settings);
        })
        .WithName("UpdateSettings");

        group.MapGet("/stats/streaks", async (AppDbContext db, IAppClock clock, CancellationToken ct) =>
        {
            var items = await db.ChecklistItems.AsNoTracking().Where(i => i.IsActive).ToListAsync(ct);
            var completions = await db.ChecklistCompletions.AsNoTracking()
                .Where(c => c.Status == CompletionStatus.Done)
                .ToListAsync(ct);
            var today = clock.Today;
            var completionsByItem = completions.GroupBy(c => c.ChecklistItemId)
                .ToDictionary(g => g.Key, g => g.Select(c => c.OccurrenceDate).OrderByDescending(d => d).ToList());

            var streaks = items.Select(item =>
            {
                var dates = completionsByItem.TryGetValue(item.Id, out var d) ? d : new List<DateOnly>();
                var streak = 0;
                var expected = today;
                foreach (var date in dates)
                {
                    if (date != expected) break;
                    streak++;
                    expected = expected.AddDays(-1);
                }
                return new { itemId = item.Id, title = item.Title, currentStreak = streak };
            }).OrderByDescending(s => s.currentStreak).ToList();

            return Results.Ok(streaks);
        })
        .WithName("GetStreaks");

        group.MapGet("/stats/completion", async (AppDbContext db, DateOnly from, DateOnly to, CancellationToken ct) =>
        {
            var completions = await db.ChecklistCompletions.AsNoTracking()
                .Where(c => c.OccurrenceDate >= from && c.OccurrenceDate <= to)
                .ToListAsync(ct);

            var byDate = completions
                .GroupBy(c => c.OccurrenceDate)
                .OrderBy(g => g.Key)
                .Select(g => new
                {
                    date = g.Key,
                    done = g.Count(c => c.Status == CompletionStatus.Done),
                    skipped = g.Count(c => c.Status == CompletionStatus.Skipped),
                    partial = g.Count(c => c.Status == CompletionStatus.Partial)
                });

            return Results.Ok(byDate);
        })
        .WithName("GetCompletionStats");

        group.MapGet("/export", async (AppDbContext db, CancellationToken ct) =>
        {
            var backup = new
            {
                exportedAtUtc = DateTimeOffset.UtcNow,
                checklists = await db.Checklists.AsNoTracking().ToListAsync(ct),
                checklistItems = await db.ChecklistItems.AsNoTracking().ToListAsync(ct),
                checklistCompletions = await db.ChecklistCompletions.AsNoTracking().ToListAsync(ct),
                timetableTemplates = await db.TimetableTemplates.AsNoTracking().ToListAsync(ct),
                timetableBlocks = await db.TimetableBlocks.AsNoTracking().ToListAsync(ct),
                timetableAssignments = await db.TimetableAssignments.AsNoTracking().ToListAsync(ct),
                dayOverrides = await db.DayOverrides.AsNoTracking().ToListAsync(ct),
                futureTasks = await db.FutureTasks.AsNoTracking().ToListAsync(ct),
                reminders = await db.Reminders.AsNoTracking().ToListAsync(ct),
                simpleTasks = await db.SimpleTasks.AsNoTracking().ToListAsync(ct),
                settings = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(ct)
            };

            return Results.Ok(backup);
        })
        .WithName("ExportBackup");

        group.MapPost("/import", () =>
            // TODO: full import is intentionally out of scope for this scaffold — it needs
            // careful id-collision handling (re-parent FKs vs. overwrite) that deserves its own
            // design pass rather than a guessed implementation. Wire this up in Phase 7 alongside
            // export, per plan section 7.
            Results.Problem(
                title: "Not implemented",
                detail: "POST /api/v1/import is a Phase 7 item — see plan section 7 (Polish & extras).",
                statusCode: StatusCodes.Status501NotImplemented))
        .WithName("ImportBackup");

        return app;
    }

    private static async Task<AppSetting> GetOrCreateSettingsAsync(AppDbContext db, IAppClockFactory clocks, CancellationToken ct, bool tracked = false)
    {
        var query = tracked ? db.AppSettings : db.AppSettings.AsNoTracking();
        var settings = await query.FirstOrDefaultAsync(ct);
        if (settings is not null)
            return settings;

        settings = new AppSetting { TimeZone = clocks.DefaultTimeZoneId }; // UserId stamped on save
        db.AppSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }
}
