using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

/// <summary>Frozen clock: Monday 2026-10-05 10:30 (+05:30).</summary>
public class TodayApiTests : IntegrationTestBase
{
    public TodayApiTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<(Guid TemplateId, Guid BlockNow, Guid BlockNext, Guid ItemLinked, Guid ItemFixed)> SeedDayAsync()
    {
        var template = await PostCreatedAsync("/api/v1/timetable/templates", new { name = "Weekday", dayStart = "00:00:00", dayEnd = "23:00:00", slotMinutes = 30 });
        await PatchOkAsync($"/api/v1/timetable/templates/{Id(template)}/default");
        var past = await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "Gym", startTime = "07:00:00", endTime = "08:00:00", category = "Health", allowOverlap = false, notifyAtStart = false, sortOrder = 0 });
        var now = await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "Deep work ✓", startTime = "10:00:00", endTime = "11:00:00", category = "Work", allowOverlap = false, notifyAtStart = false, sortOrder = 0 });
        var next = await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "Lunch", startTime = "12:00:00", endTime = "13:00:00", category = "Break", allowOverlap = false, notifyAtStart = false, sortOrder = 0 });

        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "Morning", color = "#f59e0b" });
        var linked = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "Stretch", anchorType = "LinkedToBlock", timetableBlockId = Id(past), recurrence = new { type = "Weekly", daysOfWeek = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" } } });
        var fixedItem = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "Vitamins", anchorType = "FixedTime", anchorTime = "09:00:00", recurrence = new { type = "Daily" } });
        await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "Not today", recurrence = new { type = "Weekly", daysOfWeek = new[] { "Sunday" } } });
        await AssertStatusAsync(HttpStatusCode.OK, await Client.PostAsJsonAsync($"/api/v1/items/{Id(linked)}/complete", new { date = Today.ToString("yyyy-MM-dd") }));

        await PostCreatedAsync("/api/v1/future-tasks", new { title = "Due today", dueDate = Today.ToString("yyyy-MM-dd"), dueTime = "17:00:00", priority = "High" });
        await PostCreatedAsync("/api/v1/future-tasks", new { title = "Overdue", dueDate = Today.AddDays(-3).ToString("yyyy-MM-dd") });
        return (Id(template), Id(now), Id(next), Id(linked), Id(fixedItem));
    }

    [Fact]
    public async Task Today_AssemblesFullPlanFromPostgres()
    {
        var seeded = await SeedDayAsync();
        var today = await GetOkAsync("/api/v1/today");

        Assert.Equal("2026-10-05", Str(today, "date"));
        Assert.Equal("Monday", Str(today, "dayOfWeek"));
        Assert.Equal(seeded.TemplateId, today.GetProperty("template").GetProperty("id").GetGuid());
        Assert.Equal("00:00:00", Str(today.GetProperty("template"), "dayStart"));

        var blocks = today.GetProperty("blocks").EnumerateArray().ToList();
        Assert.Equal(new[] { "past", "current", "upcoming" }, blocks.Select(b => Str(b, "state")));
        Assert.True(blocks[0].GetProperty("linkedItems")[0].GetProperty("isCompleted").GetBoolean());
        Assert.Equal(seeded.BlockNow, today.GetProperty("nowBlock").GetProperty("blockId").GetGuid());
        Assert.Equal(50, today.GetProperty("nowBlock").GetProperty("progressPercent").GetInt32());
        Assert.Equal(seeded.BlockNext, today.GetProperty("nextBlock").GetProperty("blockId").GetGuid());

        var group = today.GetProperty("checklists").EnumerateArray().Single();
        Assert.Equal(2, group.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, group.GetProperty("completedCount").GetInt32());
        var vitamins = group.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("itemId").GetGuid() == seeded.ItemFixed);
        Assert.True(vitamins.GetProperty("isOverdue").GetBoolean());
        Assert.Equal("Every day", Str(vitamins, "recurrenceLabel"));

        Assert.Equal("Due today", Str(today.GetProperty("dueToday")[0], "title"));
        Assert.Equal("Overdue", Str(today.GetProperty("overdue")[0], "title"));
        Assert.Equal(50, today.GetProperty("summary").GetProperty("completionPercent").GetInt32());
        Assert.Equal(180, today.GetProperty("summary").GetProperty("minutesScheduled").GetInt32());
    }

    [Fact]
    public async Task Today_Now_Summary_AndRange()
    {
        var seeded = await SeedDayAsync();

        var now = await GetOkAsync("/api/v1/today/now");
        Assert.Equal(seeded.BlockNow, now.GetProperty("nowBlock").GetProperty("blockId").GetGuid());

        var summary = await GetOkAsync("/api/v1/days/2026-10-05/summary");
        Assert.Equal(2, summary.GetProperty("totalItems").GetInt32());
        Assert.Equal(3, summary.GetProperty("blocksTotal").GetInt32());

        var week = (await GetOkAsync("/api/v1/days/range?from=2026-10-04&to=2026-10-10")).EnumerateArray().ToList();
        Assert.Equal(7, week.Count);
        Assert.Equal("2026-10-04", Str(week[0], "date"));
        Assert.Equal(2, week[0].GetProperty("summary").GetProperty("totalItems").GetInt32()); // Sunday: Daily + Sunday-only
        Assert.Equal(2, week[1].GetProperty("summary").GetProperty("totalItems").GetInt32()); // Monday: weekday + Daily
    }

    [Fact]
    public async Task Range_Caps_AndValidation()
    {
        Assert.Equal(367, (await GetOkAsync("/api/v1/days/range?from=2026-01-01&to=2027-01-02")).GetArrayLength());
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/days/range?from=2026-01-01&to=2027-01-03"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/days/range?from=2026-01-02&to=2026-01-01"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/days/range?from=2026-01-01"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/days/not-a-date/summary"));
    }

    [Fact]
    public async Task Today_OverridesAndEmptyDays()
    {
        var empty = await GetOkAsync("/api/v1/today?date=0001-01-01");
        Assert.Equal("(none)", Str(empty.GetProperty("template"), "name"));
        await GetOkAsync("/api/v1/today?date=9999-12-31");

        await SeedDayAsync();
        await PutOkAsync("/api/v1/day-overrides/2026-10-05", new { mode = "RestDay", note = "Holiday 🎉" });
        var rest = await GetOkAsync("/api/v1/today");
        Assert.Empty(rest.GetProperty("blocks").EnumerateArray());
        Assert.Equal("RestDay", Str(rest.GetProperty("override"), "mode"));
        Assert.Equal("Holiday 🎉", Str(rest.GetProperty("override"), "note"));
        Assert.Single(rest.GetProperty("checklists").EnumerateArray()); // checklists unaffected

        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/today?date=2026-02-30"));
    }

    [Fact]
    public async Task Today_IgnoresArchivedChecklists_AndInactiveItems()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        var item = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "x", recurrence = new { type = "Daily" } });
        Assert.Single((await GetOkAsync("/api/v1/today")).GetProperty("checklists").EnumerateArray());

        await PatchOkAsync($"/api/v1/items/{Id(item)}/active?active=false");
        Assert.Empty((await GetOkAsync("/api/v1/today")).GetProperty("checklists").EnumerateArray());
        await PatchOkAsync($"/api/v1/items/{Id(item)}/active?active=true");
        await PatchOkAsync($"/api/v1/checklists/{Id(checklist)}/archive?archived=true");
        Assert.Empty((await GetOkAsync("/api/v1/today")).GetProperty("checklists").EnumerateArray());
    }
}
