using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class ChecklistsApiTests : IntegrationTestBase
{
    private const string Url = "/api/v1/checklists";

    public ChecklistsApiTests(PostgresFixture fixture) : base(fixture) { }

    private Task<JsonElement> CreateChecklistAsync(string name = "Morning") => PostCreatedAsync(Url, new { name });

    private async Task<Guid> CreateBlockAsync()
    {
        var template = await PostCreatedAsync("/api/v1/timetable/templates", new { name = "T", dayStart = "06:00:00", dayEnd = "23:00:00", slotMinutes = 30 });
        var block = await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new
        {
            title = "Gym", startTime = "07:00:00", endTime = "08:00:00", category = "Health", allowOverlap = false, notifyAtStart = false, sortOrder = 0
        });
        return Id(block);
    }

    [Fact]
    public async Task Checklist_Create_Get_List_RoundTrip()
    {
        var created = await PostCreatedAsync(Url, new { name = "  Morning ✓  ", description = Text.Of(3000), color = "#f59e0b80", icon = Text.Of(40), sortOrder = 2 });
        Assert.Equal("Morning ✓", Str(created, "name"));

        var get = await GetOkAsync($"{Url}/{Id(created)}");
        AssertJsonEquivalent(created, get);
        Assert.Empty(get.GetProperty("items").EnumerateArray());

        var listed = (await GetOkAsync(Url)).EnumerateArray().Single();
        AssertJsonEquivalent(created, listed);
    }

    [Fact]
    public async Task Checklist_Validation_Boundaries_And404s()
    {
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Post, Url, "{\"name\":\"  \"}"), "name");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, Url, "{\"description\":\"x\"}"));
        await PostCreatedAsync(Url, new { name = Text.Emoji(120), color = "#12345678" });
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Url, new { name = Text.Of(121) }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Url, new { name = "x", color = "#1234567890" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Url, new { name = "x", icon = Text.Of(41) }));

        var unknown = Guid.NewGuid();
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{Url}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{Url}/{unknown}", new { name = "x" }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsync($"{Url}/{unknown}/archive?archived=true", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Url}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsJsonAsync($"{Url}/{unknown}/items", new { title = "x" }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{Url}/{unknown}/items"));
    }

    [Fact]
    public async Task Checklist_Update_Archive_Delete()
    {
        var created = await CreateChecklistAsync();
        var updated = await PutOkAsync($"{Url}/{Id(created)}", new { name = "Evening", description = (string?)null, color = "#000", icon = "moon", sortOrder = 9 });
        AssertJsonEquivalent(updated, await GetOkAsync($"{Url}/{Id(created)}"));
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { name = "" }), "name");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { name = Text.Of(121) }));

        var archived = await PatchOkAsync($"{Url}/{Id(created)}/archive?archived=true");
        Assert.True(archived.GetProperty("isArchived").GetBoolean());
        Assert.Empty((await GetOkAsync(Url)).EnumerateArray());
        Assert.Single((await GetOkAsync($"{Url}?includeArchived=true")).EnumerateArray());
        await PatchOkAsync($"{Url}/{Id(created)}/archive?archived=false");
        Assert.Single((await GetOkAsync(Url)).EnumerateArray());

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Url}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{Url}/{Id(created)}"));
    }

    [Fact]
    public async Task Checklist_Reorder()
    {
        var a = await CreateChecklistAsync("a");
        var b = await CreateChecklistAsync("b");
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PutAsJsonAsync($"{Url}/reorder", new
        {
            items = new[] { new { id = Id(a), sortOrder = 5 }, new { id = Id(b), sortOrder = 1 }, new { id = Guid.NewGuid(), sortOrder = 0 } }
        }));
        Assert.Equal(new[] { Id(b), Id(a) }, (await GetOkAsync(Url)).EnumerateArray().Select(Id));
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Put, $"{Url}/reorder", "{\"items\":null}"), "items");
    }

    [Fact]
    public async Task Item_FullyPopulated_RoundTrips_WithRecurrenceJsonb()
    {
        var checklist = await CreateChecklistAsync();
        var blockId = await CreateBlockAsync();
        var created = await PostCreatedAsync($"{Url}/{Id(checklist)}/items", new
        {
            title = Text.Of(200),
            notes = Text.Emoji(500),
            priority = "Critical",
            estimatedMinutes = 45,
            anchorType = "LinkedToBlock",
            anchorTime = "07:15:00",
            windowStart = "06:00:00",
            windowEnd = "09:30:00",
            timetableBlockId = blockId,
            recurrence = new
            {
                type = "MonthlyByWeekday", interval = 2, daysOfWeek = new[] { "Monday", "Friday" }, dayOfMonth = 31,
                nthWeekday = new { nth = -1, dayOfWeek = "Friday" }, startDate = "2026-01-01", endDate = "2027-12-31",
                exceptionDates = new[] { "2026-12-25" }
            },
            dueDate = "2026-02-28",
            reminderOffsetMinutes = 10080
        });
        Assert.Equal(0, created.GetProperty("sortOrder").GetInt32());

        var listed = (await GetOkAsync($"{Url}/{Id(checklist)}/items")).EnumerateArray().Single();
        AssertJsonEquivalent(created, listed);
        var inChecklist = (await GetOkAsync($"{Url}/{Id(checklist)}")).GetProperty("items").EnumerateArray().Single();
        AssertJsonEquivalent(created, inChecklist);

        var second = await PostCreatedAsync($"{Url}/{Id(checklist)}/items", new { title = "second" });
        Assert.Equal(1, second.GetProperty("sortOrder").GetInt32());
        Assert.Equal("None", second.GetProperty("recurrence").GetProperty("type").GetString());
    }

    [Fact]
    public async Task Item_Validation_FkAndBoundaries()
    {
        var checklist = await CreateChecklistAsync();
        var itemsUrl = $"{Url}/{Id(checklist)}/items";

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(itemsUrl, new { title = " " }), "title");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(itemsUrl, new { title = "x", timetableBlockId = Guid.NewGuid() }), "timetableBlockId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(itemsUrl, new { title = Text.Of(201) }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, itemsUrl, "{\"title\":\"x\",\"anchorTime\":\"25:00\"}"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, itemsUrl, "{\"title\":\"x\",\"recurrence\":{\"type\":\"Hourly\"}}"));
        await PostCreatedAsync(itemsUrl, new { title = Text.Emoji(200), anchorTime = "23:59:59", dueDate = "9999-12-31" });
        await PostCreatedAsync(itemsUrl, new { title = "min date", dueDate = "0001-01-01" });
    }

    [Fact]
    public async Task Item_Update_SetActive_Delete_And404s()
    {
        var checklist = await CreateChecklistAsync();
        var item = await PostCreatedAsync($"{Url}/{Id(checklist)}/items", new { title = "x" });
        var itemUrl = $"/api/v1/items/{Id(item)}";

        var updated = await PutOkAsync(itemUrl, new
        {
            title = "y", priority = "Low", anchorType = "FixedTime", anchorTime = "05:00:00",
            recurrence = new { type = "Weekly", daysOfWeek = new[] { "Saturday" } }, isActive = false
        });
        Assert.False(updated.GetProperty("isActive").GetBoolean());
        AssertJsonEquivalent(updated, (await GetOkAsync($"{Url}/{Id(checklist)}/items")).EnumerateArray().Single());

        await AssertValidationErrorAsync(await Client.PutAsJsonAsync(itemUrl, new { title = "" }), "title");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync(itemUrl, new { title = "x", timetableBlockId = Guid.NewGuid() }), "timetableBlockId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync(itemUrl, new { title = Text.Of(201) }));

        var active = await PatchOkAsync($"{itemUrl}/active?active=true");
        Assert.True(active.GetProperty("isActive").GetBoolean());

        var unknown = $"/api/v1/items/{Guid.NewGuid()}";
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync(unknown, new { title = "x" }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsync($"{unknown}/active?active=true", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync(unknown));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync(itemUrl));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync(itemUrl));
    }

    [Fact]
    public async Task Item_Reorder_OnlyWithinChecklist()
    {
        var c1 = await CreateChecklistAsync("c1");
        var c2 = await CreateChecklistAsync("c2");
        var a = await PostCreatedAsync($"{Url}/{Id(c1)}/items", new { title = "a" });
        var b = await PostCreatedAsync($"{Url}/{Id(c1)}/items", new { title = "b" });
        var other = await PostCreatedAsync($"{Url}/{Id(c2)}/items", new { title = "other" });

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PutAsJsonAsync($"{Url}/{Id(c1)}/items/reorder", new
        {
            items = new[] { new { id = Id(a), sortOrder = 3 }, new { id = Id(b), sortOrder = 0 }, new { id = Id(other), sortOrder = 99 } }
        }));
        Assert.Equal(new[] { Id(b), Id(a) }, (await GetOkAsync($"{Url}/{Id(c1)}/items")).EnumerateArray().Select(Id));
        Assert.Equal(0, (await GetOkAsync($"{Url}/{Id(c2)}/items")).EnumerateArray().Single().GetProperty("sortOrder").GetInt32());
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Put, $"{Url}/{Id(c1)}/items/reorder", "{\"items\":null}"), "items");
    }

    [Fact]
    public async Task Complete_Skip_Uncomplete_History_AndCounts()
    {
        var checklist = await CreateChecklistAsync();
        var item = await PostCreatedAsync($"{Url}/{Id(checklist)}/items", new { title = "x", recurrence = new { type = "Daily" } });
        var itemUrl = $"/api/v1/items/{Id(item)}";
        var today = Today.ToString("yyyy-MM-dd");
        var yesterday = Today.AddDays(-1).ToString("yyyy-MM-dd");

        var done = await ReadJsonAsync(await Client.PostAsJsonAsync($"{itemUrl}/complete", new { date = today, note = "Ünïcødé ✓" }));
        Assert.Equal("Done", Str(done, "status"));
        // Completing twice is an upsert on (item, date), not a unique-index violation.
        var partial = await ReadJsonAsync(await Client.PostAsJsonAsync($"{itemUrl}/complete", new { date = today, status = "Partial" }));
        Assert.Equal(Id(done), Id(partial));
        Assert.Equal("Partial", Str(partial, "status"));
        await PostOkAsync($"{itemUrl}/complete", new { date = today });

        var skipped = await PostOkAsync($"{itemUrl}/skip", new { date = yesterday, reason = "sick" });
        Assert.Equal("Skipped", Str(skipped, "status"));
        Assert.Equal("sick", Str(skipped, "note"));

        Assert.Equal(1, (await GetOkAsync(Url)).EnumerateArray().Single().GetProperty("completedTodayCount").GetInt32());

        var history = await GetOkAsync($"{itemUrl}/history?from={yesterday}&to={today}");
        var entries = history.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(new[] { today, yesterday }, entries.Select(e => Str(e, "occurrenceDate")));
        Assert.Equal(1, history.GetProperty("currentStreak").GetInt32());

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{itemUrl}/complete?date={today}"));
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{itemUrl}/complete?date={today}")); // idempotent
        Assert.Single((await GetOkAsync($"{itemUrl}/history?from={yesterday}&to={today}")).GetProperty("history").EnumerateArray());

        var unknown = $"/api/v1/items/{Guid.NewGuid()}";
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsJsonAsync($"{unknown}/complete", new { date = today }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsJsonAsync($"{unknown}/skip", new { date = today }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, $"{itemUrl}/complete", "{\"status\":\"Done\"}"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync($"{itemUrl}/history?from={today}"));
    }

    [Fact]
    public async Task PreviewOccurrences_RangeCapAndValidation()
    {
        const string url = "/api/v1/items/preview-occurrences";
        var weekly = await PostOkArrayAsync(url, new { recurrence = new { type = "Weekly", daysOfWeek = new[] { "Monday" } }, from = "2026-10-01", to = "2026-10-31" });
        Assert.Equal(new[] { "2026-10-05", "2026-10-12", "2026-10-19", "2026-10-26" }, weekly.EnumerateArray().Select(d => d.GetString()));

        var atCap = await PostOkArrayAsync(url, new { recurrence = new { type = "Daily" }, from = "2026-01-01", to = new DateOnly(2026, 1, 1).AddDays(3660).ToString("yyyy-MM-dd") });
        Assert.Equal(3661, atCap.GetArrayLength());

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { recurrence = new { type = "Daily" }, from = "2026-01-01", to = new DateOnly(2026, 1, 1).AddDays(3661).ToString("yyyy-MM-dd") }), "to");
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Post, url, "{\"recurrence\":null,\"from\":\"2026-01-01\",\"to\":\"2026-01-02\"}"), "recurrence");
    }

    private async Task<JsonElement> PostOkAsync(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadJsonAsync(response);
    }

    private Task<JsonElement> PostOkArrayAsync(string url, object body) => PostOkAsync(url, body);
}
