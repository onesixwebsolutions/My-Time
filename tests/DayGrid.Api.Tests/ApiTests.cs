using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace DayGrid.Api.Tests;

public class HealthTests : ApiTestBase
{
    public HealthTests(DayGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Health_Returns200_Healthy()
    {
        var response = await Client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", (await ReadJsonAsync(response)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task HealthReady_Returns200_WhenDbContextIsReachable()
    {
        var response = await Client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/does-not-exist")]
    [InlineData("POST", "/api/v1/nope/123")]
    [InlineData("GET", "/api")]
    public async Task UnknownApiRoute_Returns404_NotTheSpaFallback(string method, string url)
    {
        var response = await Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }
}

public class TasksEndpointTests : ApiTestBase
{
    private const string Url = "/api/v1/tasks";

    public TasksEndpointTests(DayGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Crud_HappyPath()
    {
        // Create
        var create = await Client.PostAsJsonAsync(Url, new { title = "  Buy milk  ", notes = "2L" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await ReadJsonAsync(create);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/api/v1/tasks/{id}", create.Headers.Location!.OriginalString);
        Assert.Equal("Buy milk", created.GetProperty("title").GetString());
        Assert.Equal("2L", created.GetProperty("notes").GetString());

        // List
        var list = await ReadJsonAsync(await Client.GetAsync(Url));
        Assert.Contains(list.EnumerateArray(), t => t.GetProperty("id").GetGuid() == id);

        // Update
        var update = await Client.PutAsJsonAsync($"{Url}/{id}", new { title = "Buy oat milk", notes = (string?)null, priority = "High" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var updated = await ReadJsonAsync(update);
        Assert.Equal("Buy oat milk", updated.GetProperty("title").GetString());
        Assert.Equal("High", updated.GetProperty("priority").GetString());

        // Status
        var status = await Client.PatchAsJsonAsync($"{Url}/{id}/status", new { status = "Done" });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var done = await ReadJsonAsync(status);
        Assert.Equal("Done", done.GetProperty("status").GetString());
        Assert.NotEqual(JsonValueKind.Null, done.GetProperty("completedAt").ValueKind);

        // Delete
        Assert.Equal(HttpStatusCode.NoContent, (await Client.DeleteAsync($"{Url}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"{Url}/{id}")).StatusCode);
    }

    [Fact]
    public async Task Create_SerializesEnumsAsPascalCaseStrings_AndPropertiesAsCamelCase()
    {
        var response = await Client.PostAsJsonAsync(Url, new { title = "Enum check" });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Contains("\"status\":\"Open\"", raw);
        Assert.Contains("\"priority\":\"Normal\"", raw);
        Assert.Contains("\"sortOrder\":", raw);
        Assert.DoesNotContain("\"Status\"", raw);
    }

    [Fact]
    public async Task Create_AcceptsEnumAsString()
    {
        var created = await PostCreatedAsync(Url, new { title = "Urgent", priority = "Critical" });

        Assert.Equal("Critical", created.GetProperty("priority").GetString());
    }

    [Theory]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"   \"}")]
    public async Task Create_NullOrBlankTitle_Returns400WithTitleError(string body)
    {
        var response = await PostRawJsonAsync(Url, body);

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task Create_MissingRequiredTitle_Returns400()
    {
        var response = await PostRawJsonAsync(Url, "{\"notes\":\"no title\"}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_NullTitle_Returns400()
    {
        var created = await PostCreatedAsync(Url, new { title = "to update" });

        var response = await PutRawJsonAsync($"{Url}/{created.GetProperty("id").GetGuid()}", "{\"title\":null,\"priority\":\"Low\"}");

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task UnknownId_Returns404()
    {
        var unknown = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PutAsJsonAsync($"{Url}/{unknown}", new { title = "x", priority = "Low" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Client.PatchAsJsonAsync($"{Url}/{unknown}/status", new { status = "Done" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client.DeleteAsync($"{Url}/{unknown}")).StatusCode);
    }

    [Fact]
    public async Task Reorder_WithDuplicateIds_LastEntryWins()
    {
        var created = await PostCreatedAsync(Url, new { title = "reorder me" });
        var id = created.GetProperty("id").GetGuid();

        var response = await Client.PutAsJsonAsync($"{Url}/reorder", new
        {
            items = new[] { new { id, sortOrder = 5 }, new { id, sortOrder = 42 } }
        });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var list = await ReadJsonAsync(await Client.GetAsync(Url));
        var task = list.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == id);
        Assert.Equal(42, task.GetProperty("sortOrder").GetInt32());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("{\"title\": \"unterminated")]
    [InlineData("[1,2,3]")]
    [InlineData("{\"title\":\"x\",\"priority\":\"NotAPriority\"}")]
    public async Task MalformedJson_Returns400(string body)
    {
        var response = await PostRawJsonAsync(Url, body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}

public class ChecklistsEndpointTests : ApiTestBase
{
    private const string Url = "/api/v1/checklists";

    public ChecklistsEndpointTests(DayGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task CreateThenGet_ReturnsChecklistWithItems()
    {
        var created = await PostCreatedAsync(Url, new { name = " Morning ", color = "#ff0000", sortOrder = 3 });
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Morning", created.GetProperty("name").GetString());
        Assert.Equal(0, created.GetProperty("itemCount").GetInt32());

        var item = await PostCreatedAsync($"{Url}/{id}/items", new
        {
            title = "Meditate",
            priority = "High",
            anchorType = "FixedTime",
            anchorTime = "07:00:00",
            recurrence = new { type = "Daily", interval = 1 }
        });
        Assert.Equal("FixedTime", item.GetProperty("anchorType").GetString());
        Assert.Equal("Daily", item.GetProperty("recurrence").GetProperty("type").GetString());

        var get = await Client.GetAsync($"{Url}/{id}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var json = await ReadJsonAsync(get);
        Assert.Equal("Morning", json.GetProperty("name").GetString());
        Assert.Equal("#ff0000", json.GetProperty("color").GetString());
        Assert.Equal(1, json.GetProperty("itemCount").GetInt32());
        var items = json.GetProperty("items").EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Meditate", items[0].GetProperty("title").GetString());
        Assert.Equal("07:00:00", items[0].GetProperty("anchorTime").GetString());
    }

    [Theory]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"\"}")]
    public async Task Create_NullOrEmptyName_Returns400(string body)
    {
        await AssertValidationErrorAsync(await PostRawJsonAsync(Url, body), "name");
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client.GetAsync($"{Url}/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task CreateItem_UnknownChecklist_Returns404()
    {
        var response = await Client.PostAsJsonAsync($"{Url}/{Guid.NewGuid()}/items", new { title = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateItem_NullTitle_Returns400()
    {
        var checklist = await PostCreatedAsync(Url, new { name = "List" });

        var response = await PostRawJsonAsync($"{Url}/{checklist.GetProperty("id").GetGuid()}/items", "{\"title\":null}");

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task CreateItem_UnknownTimetableBlock_Returns400()
    {
        var checklist = await PostCreatedAsync(Url, new { name = "List" });

        var response = await Client.PostAsJsonAsync($"{Url}/{checklist.GetProperty("id").GetGuid()}/items",
            new { title = "Linked", anchorType = "LinkedToBlock", timetableBlockId = Guid.NewGuid() });

        await AssertValidationErrorAsync(response, "timetableBlockId");
    }

    [Fact]
    public async Task CompleteItem_UnknownItem_Returns404()
    {
        var response = await Client.PostAsJsonAsync($"/api/v1/items/{Guid.NewGuid()}/complete", new { date = "2026-10-05" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class TimetableEndpointTests : ApiTestBase
{
    public TimetableEndpointTests(DayGridApiFactory factory) : base(factory) { }

    private Task<JsonElement> CreateTemplateAsync(string name = "Weekday") =>
        PostCreatedAsync("/api/v1/timetable/templates", new { name, dayStart = "06:00:00", dayEnd = "23:00:00", slotMinutes = 30 });

    [Fact]
    public async Task CreateTemplate_NullName_Returns400()
    {
        var response = await PostRawJsonAsync("/api/v1/timetable/templates",
            "{\"name\":null,\"dayStart\":\"06:00:00\",\"dayEnd\":\"23:00:00\",\"slotMinutes\":30}");

        await AssertValidationErrorAsync(response, "name");
    }

    [Fact]
    public async Task CreateAssignment_UnknownTemplate_Returns400()
    {
        var response = await Client.PostAsJsonAsync("/api/v1/timetable/assignments",
            new { templateId = Guid.NewGuid(), scope = "Weekday", dayOfWeek = "Monday", priority = 0 });

        await AssertValidationErrorAsync(response, "templateId");
    }

    [Fact]
    public async Task CreateAssignment_KnownTemplate_Returns201_WithEnumStrings()
    {
        var template = await CreateTemplateAsync();

        var created = await PostCreatedAsync("/api/v1/timetable/assignments",
            new { templateId = template.GetProperty("id").GetGuid(), scope = "Weekday", dayOfWeek = "Monday", priority = 1 });

        Assert.Equal("Weekday", created.GetProperty("scope").GetString());
        Assert.Equal("Monday", created.GetProperty("dayOfWeek").GetString());
    }

    [Fact]
    public async Task CreateBlock_UnknownTemplate_Returns404()
    {
        var response = await Client.PostAsJsonAsync($"/api/v1/timetable/templates/{Guid.NewGuid()}/blocks",
            new { title = "Work", startTime = "09:00:00", endTime = "10:00:00", category = "Work" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateBlock_UnknownChecklist_Returns400()
    {
        var template = await CreateTemplateAsync();

        var response = await Client.PostAsJsonAsync($"/api/v1/timetable/templates/{template.GetProperty("id").GetGuid()}/blocks",
            new { title = "Work", startTime = "09:00:00", endTime = "10:00:00", category = "Work", checklistId = Guid.NewGuid() });

        await AssertValidationErrorAsync(response, "checklistId");
    }

    [Fact]
    public async Task CreateBlock_EndBeforeStart_Returns400()
    {
        var template = await CreateTemplateAsync();

        var response = await Client.PostAsJsonAsync($"/api/v1/timetable/templates/{template.GetProperty("id").GetGuid()}/blocks",
            new { title = "Backwards", startTime = "10:00:00", endTime = "09:00:00", category = "Work" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateBlock_Overlap_Returns409()
    {
        var template = await CreateTemplateAsync();
        var url = $"/api/v1/timetable/templates/{template.GetProperty("id").GetGuid()}/blocks";
        await PostCreatedAsync(url, new { title = "A", startTime = "09:00:00", endTime = "10:00:00", category = "Work" });

        var response = await Client.PostAsJsonAsync(url, new { title = "B", startTime = "09:30:00", endTime = "10:30:00", category = "Work" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpsertDayOverride_UnknownTemplate_Returns400()
    {
        var response = await Client.PutAsJsonAsync("/api/v1/day-overrides/2026-12-25",
            new { mode = "UseTemplate", templateId = Guid.NewGuid() });

        await AssertValidationErrorAsync(response, "templateId");
    }

    [Fact]
    public async Task UpdateBlock_UnknownBlock_Returns404()
    {
        var response = await Client.PutAsJsonAsync($"/api/v1/timetable/blocks/{Guid.NewGuid()}",
            new { title = "x", startTime = "09:00:00", endTime = "10:00:00", category = "Work" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public class FutureTasksEndpointTests : ApiTestBase
{
    private const string Url = "/api/v1/future-tasks";

    public FutureTasksEndpointTests(DayGridApiFactory factory) : base(factory) { }

    private static DateTimeOffset FireAt(JsonElement task) =>
        task.GetProperty("reminders").EnumerateArray().Single().GetProperty("fireAtUtc").GetDateTimeOffset();

    [Fact]
    public async Task Create_ReminderFireTime_IsComputedInUserZone()
    {
        // Clock zone is +05:30: 2026-10-10 09:00 local = 03:30 UTC; 30 min before = 03:00 UTC.
        var created = await PostCreatedAsync(Url, new
        {
            title = "Dentist",
            dueDate = "2026-10-10",
            dueTime = "09:00:00",
            priority = "High",
            reminders = new[] { new { offsetMinutes = 30, channels = new[] { "Email", "InApp" } } }
        });

        Assert.Equal(new DateTimeOffset(2026, 10, 10, 3, 0, 0, TimeSpan.Zero), FireAt(created));
        Assert.Equal("Pending", created.GetProperty("status").GetString());
        var channels = created.GetProperty("reminders")[0].GetProperty("channels").GetString()!;
        Assert.Contains("InApp", channels);
        Assert.Contains("Email", channels);
    }

    [Fact]
    public async Task Create_WithoutDueTime_RemindsAt0900Local_DefaultChannelInApp()
    {
        var created = await PostCreatedAsync(Url, new
        {
            title = "Renew passport",
            dueDate = "2026-11-01",
            priority = "Normal",
            reminders = new[] { new { offsetMinutes = 0, channels = Array.Empty<string>() } }
        });

        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 30, 0, TimeSpan.Zero), FireAt(created));
        Assert.Equal("InApp", created.GetProperty("reminders")[0].GetProperty("channels").GetString());
    }

    [Fact]
    public async Task Defer_RecomputesScheduledReminderFireTime()
    {
        var created = await PostCreatedAsync(Url, new
        {
            title = "Call bank",
            dueDate = "2026-10-10",
            dueTime = "18:00:00",
            priority = "Normal",
            reminders = new[] { new { offsetMinutes = 60, channels = new[] { "InApp" } } }
        });

        var response = await Client.PatchAsJsonAsync($"{Url}/{created.GetProperty("id").GetGuid()}/defer", new { newDueDate = "2026-10-12" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var deferred = await ReadJsonAsync(response);
        Assert.Equal("Deferred", deferred.GetProperty("status").GetString());
        // 18:00 local (+05:30) = 12:30 UTC, minus 60 min = 11:30 UTC on the new date.
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 11, 30, 0, TimeSpan.Zero), FireAt(deferred));
    }

    [Fact]
    public async Task Create_NullTitle_Returns400()
    {
        var response = await PostRawJsonAsync(Url, "{\"title\":null,\"dueDate\":\"2026-10-10\",\"priority\":\"Normal\"}");

        await AssertValidationErrorAsync(response, "title");
    }

    [Fact]
    public async Task Update_UnknownId_Returns404()
    {
        var response = await Client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { title = "x", dueDate = "2026-10-10", priority = "Normal" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

/// <summary>Own fixture instance (own InMemory DB), so the database is guaranteed empty.</summary>
public class TodayEndpointTests : ApiTestBase
{
    public TodayEndpointTests(DayGridApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Today_OnEmptyDb_Returns200_WithExpectedShape()
    {
        var response = await Client.GetAsync("/api/v1/today");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await ReadJsonAsync(response);

        // Frozen clock: Monday 2026-10-05 in the user's zone.
        Assert.Equal("2026-10-05", json.GetProperty("date").GetString());
        Assert.Equal("Monday", json.GetProperty("dayOfWeek").GetString());
        Assert.Equal(JsonValueKind.String, json.GetProperty("displayDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("override").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nowBlock").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nextBlock").ValueKind);

        var template = json.GetProperty("template");
        Assert.Equal(Guid.Empty, template.GetProperty("id").GetGuid());
        Assert.Equal("(none)", template.GetProperty("name").GetString());
        Assert.Equal(30, template.GetProperty("slotMinutes").GetInt32());

        foreach (var list in new[] { "blocks", "checklists", "dueToday", "overdue" })
            Assert.Equal(0, json.GetProperty(list).GetArrayLength());

        var summary = json.GetProperty("summary");
        foreach (var field in new[] { "totalItems", "completedItems", "completionPercent", "blocksTotal", "blocksDone", "minutesScheduled" })
            Assert.Equal(0, summary.GetProperty(field).GetInt32());
    }

    [Fact]
    public async Task Today_WithExplicitDate_UsesThatDate()
    {
        var json = await ReadJsonAsync(await Client.GetAsync("/api/v1/today?date=2026-12-25"));

        Assert.Equal("2026-12-25", json.GetProperty("date").GetString());
        Assert.Equal("Friday", json.GetProperty("dayOfWeek").GetString());
    }

    [Theory]
    [InlineData("from=2026-10-10&to=2026-10-01")]
    [InlineData("from=2026-01-01&to=2027-01-03")]
    public async Task DaysRange_InvalidOrTooLong_Returns400(string query)
    {
        var response = await Client.GetAsync($"/api/v1/days/range?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DaysRange_Valid_ReturnsOneEntryPerDay()
    {
        var json = await ReadJsonAsync(await Client.GetAsync("/api/v1/days/range?from=2026-10-01&to=2026-10-07"));

        Assert.Equal(7, json.GetArrayLength());
    }
}
