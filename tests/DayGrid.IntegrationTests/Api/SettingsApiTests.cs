using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class SettingsApiTests : IntegrationTestBase
{
    private const string Url = "/api/v1/settings";

    public SettingsApiTests(PostgresFixture fixture) : base(fixture) { }

    private static object Settings(string timeZone = "Europe/Berlin", string theme = "dark", string? emailTo = "me@example.com", string? digest = null) => new
    {
        timeZone, weekStartsOn = "Sunday", dayStart = "00:00:00", dayEnd = "23:59:59", defaultSlotMinutes = 15,
        emailEnabled = false, emailTo, dailyDigestTime = digest, theme
    };

    [Fact]
    public async Task Get_ReturnsInitSqlDefaults()
    {
        var settings = await GetOkAsync(Url);
        Assert.Equal(1, settings.GetProperty("id").GetInt32());
        Assert.Equal("Asia/Kolkata", Str(settings, "timeZone"));
        Assert.Equal("Monday", Str(settings, "weekStartsOn"));
        Assert.Equal("06:00:00", Str(settings, "dayStart"));
        Assert.Equal("07:00:00", Str(settings, "dailyDigestTime"));
        Assert.Equal("system", Str(settings, "theme"));
    }

    [Fact]
    public async Task Put_RoundTrips_IncludingNullDigestAndMaxLengths()
    {
        var updated = await PutOkAsync(Url, Settings(timeZone: Text.Of(60), theme: Text.Of(20), emailTo: Text.Of(200)));
        AssertJsonEquivalent(updated, await GetOkAsync(Url));
        Assert.Equal(JsonValueKind.Null, (await GetOkAsync(Url)).GetProperty("dailyDigestTime").ValueKind);
    }

    [Fact]
    public async Task Put_RecreatesMissingRow()
    {
        await using (var db = NewDb())
        {
            db.AppSettings.RemoveRange(db.AppSettings);
            await db.SaveChangesAsync();
        }
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync(Url));

        var created = await PutOkAsync(Url, Settings(digest: "06:30:00"));
        AssertJsonEquivalent(created, await GetOkAsync(Url));
    }

    [Fact]
    public async Task Put_Validation()
    {
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync(Url, Settings(timeZone: " ")), "timeZone");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync(Url, Settings(theme: "")), "theme");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync(Url, Settings(timeZone: Text.Of(61))));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync(Url, Settings(theme: Text.Of(21))));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync(Url, Settings(emailTo: Text.Of(201))));
        Assert.Equal("Asia/Kolkata", Str(await GetOkAsync(Url), "timeZone"));
    }

    [Fact]
    public async Task Stats_StreaksAndCompletion()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        var item = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "Run", recurrence = new { type = "Daily" } });
        var idle = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "Idle" });
        foreach (var offset in new[] { 0, -1, -2, -4 })
            await AssertStatusAsync(HttpStatusCode.OK, await Client.PostAsJsonAsync($"/api/v1/items/{Id(item)}/complete", new { date = Today.AddDays(offset).ToString("yyyy-MM-dd") }));
        await AssertStatusAsync(HttpStatusCode.OK, await Client.PostAsJsonAsync($"/api/v1/items/{Id(idle)}/skip", new { date = Today.ToString("yyyy-MM-dd") }));

        var streaks = (await GetOkAsync("/api/v1/stats/streaks")).EnumerateArray().ToList();
        Assert.Equal(3, streaks.Single(s => s.GetProperty("itemId").GetGuid() == Id(item)).GetProperty("currentStreak").GetInt32());
        Assert.Equal(0, streaks.Single(s => s.GetProperty("itemId").GetGuid() == Id(idle)).GetProperty("currentStreak").GetInt32());

        var completion = (await GetOkAsync($"/api/v1/stats/completion?from={Today.AddDays(-4):yyyy-MM-dd}&to={Today:yyyy-MM-dd}")).EnumerateArray().ToList();
        Assert.Equal(4, completion.Count);
        var todayRow = completion.Single(c => Str(c, "date") == Today.ToString("yyyy-MM-dd"));
        Assert.Equal(1, todayRow.GetProperty("done").GetInt32());
        Assert.Equal(1, todayRow.GetProperty("skipped").GetInt32());
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/stats/completion?from=2026-01-01"));
    }

    [Fact]
    public async Task Export_IncludesEveryCollection_AndImportIs501()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "i", recurrence = new { type = "Weekly", daysOfWeek = new[] { "Monday" } } });
        var template = await PostCreatedAsync("/api/v1/timetable/templates", new { name = "T", dayStart = "06:00:00", dayEnd = "22:00:00", slotMinutes = 30 });
        await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "b", startTime = "08:00:00", endTime = "09:00:00", category = "Work", allowOverlap = false, notifyAtStart = false, sortOrder = 0 });
        await PostCreatedAsync("/api/v1/future-tasks", new { title = "f", dueDate = "2026-12-01", reminders = new[] { new { offsetMinutes = 0, channels = new[] { "InApp" } } } });
        await PostCreatedAsync("/api/v1/tasks", new { title = "t" });

        var export = await GetOkAsync("/api/v1/export");
        foreach (var collection in new[] { "checklists", "checklistItems", "timetableTemplates", "timetableBlocks", "futureTasks", "reminders", "simpleTasks" })
            Assert.Single(export.GetProperty(collection).EnumerateArray());
        Assert.Equal("Asia/Kolkata", Str(export.GetProperty("settings"), "timeZone"));

        await AssertStatusAsync(HttpStatusCode.NotImplemented, await Client.PostAsync("/api/v1/import", null));
    }
}
