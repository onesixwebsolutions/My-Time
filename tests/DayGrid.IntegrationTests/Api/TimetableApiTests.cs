using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class TimetableApiTests : IntegrationTestBase
{
    private const string Templates = "/api/v1/timetable/templates";
    private const string Blocks = "/api/v1/timetable/blocks";
    private const string Assignments = "/api/v1/timetable/assignments";
    private const string Overrides = "/api/v1/day-overrides";

    public TimetableApiTests(PostgresFixture fixture) : base(fixture) { }

    private Task<JsonElement> CreateTemplateAsync(string name = "Weekday", string dayStart = "06:00:00", string dayEnd = "23:00:00", int slotMinutes = 30) =>
        PostCreatedAsync(Templates, new { name, description = "d", dayStart, dayEnd, slotMinutes });

    private static object Block(string title, string start, string end, bool allowOverlap = false, Guid? checklistId = null) => new
    {
        title, startTime = start, endTime = end, category = "Work", color = "#3b82f6", location = "Office",
        checklistId, allowOverlap, notifyAtStart = true, sortOrder = 1
    };

    [Fact]
    public async Task Template_Create_Get_List_RoundTrip_IncludingMidnightDayStart()
    {
        // dayStart 00:00 is the CLR default for TimeOnly — it used to be replaced by the DB default 06:00.
        var created = await CreateTemplateAsync("Night shift ✓", dayStart: "00:00:00", dayEnd: "23:59:00", slotMinutes: 15);
        Assert.Equal("00:00:00", Str(created, "dayStart"));

        var get = await GetOkAsync($"{Templates}/{Id(created)}");
        AssertJsonEquivalent(created, get.GetProperty("template"));
        Assert.Empty(get.GetProperty("blocks").EnumerateArray());
        AssertJsonEquivalent(created, (await GetOkAsync(Templates)).EnumerateArray().Single());

        var updated = await PutOkAsync($"{Templates}/{Id(created)}", new { name = "Renamed", description = (string?)null, dayStart = "00:00:00", dayEnd = "12:00:00", slotMinutes = 60 });
        AssertJsonEquivalent(updated, (await GetOkAsync($"{Templates}/{Id(created)}")).GetProperty("template"));
    }

    [Fact]
    public async Task Template_Validation_And404s()
    {
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(Templates, new { name = " ", dayStart = "06:00:00", dayEnd = "07:00:00", slotMinutes = 30 }), "name");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Templates, new { name = Text.Of(121), dayStart = "06:00:00", dayEnd = "07:00:00", slotMinutes = 30 }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Templates, new { name = "x", dayStart = "06:00:00", dayEnd = "07:00:00", slotMinutes = 40000 }));
        await CreateTemplateAsync(Text.Emoji(120));

        var unknown = Guid.NewGuid();
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{Templates}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{Templates}/{unknown}", new { name = "x", dayStart = "06:00:00", dayEnd = "07:00:00", slotMinutes = 30 }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Templates}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsync($"{Templates}/{unknown}/duplicate", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsync($"{Templates}/{unknown}/default", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsJsonAsync($"{Templates}/{unknown}/blocks", Block("b", "08:00:00", "09:00:00")));
    }

    [Fact]
    public async Task Template_Duplicate_CopiesBlocks_AndDefaultIsExclusive()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        var source = await CreateTemplateAsync("Source", dayStart: "00:00:00");
        await PostCreatedAsync($"{Templates}/{Id(source)}/blocks", Block("Deep work", "09:00:00", "11:00:00", checklistId: Id(checklist)));
        await PostCreatedAsync($"{Templates}/{Id(source)}/blocks", Block("Lunch", "12:00:00", "13:00:00"));

        var copyResponse = await Client.PostAsync($"{Templates}/{Id(source)}/duplicate", null);
        await AssertStatusAsync(HttpStatusCode.Created, copyResponse);
        var copy = await ReadJsonAsync(copyResponse);
        Assert.Equal("Source (copy)", Str(copy, "name"));
        Assert.Equal("00:00:00", Str(copy, "dayStart"));

        var copyBlocks = (await GetOkAsync($"{Templates}/{Id(copy)}/blocks")).EnumerateArray().ToList();
        var sourceBlocks = (await GetOkAsync($"{Templates}/{Id(source)}/blocks")).EnumerateArray().ToList();
        Assert.Equal(sourceBlocks.Select(b => Str(b, "title")), copyBlocks.Select(b => Str(b, "title")));
        for (var i = 0; i < copyBlocks.Count; i++)
            AssertJsonEquivalent(sourceBlocks[i], copyBlocks[i], "id", "templateId", "template");

        var named = await ReadJsonAsync(await Client.PostAsync($"{Templates}/{Id(source)}/duplicate?name={Uri.EscapeDataString("Kopie ✓")}", null));
        Assert.Equal("Kopie ✓", Str(named, "name"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsync($"{Templates}/{Id(source)}/duplicate?name={Text.Of(121)}", null));

        await PatchOkAsync($"{Templates}/{Id(source)}/default");
        await PatchOkAsync($"{Templates}/{Id(copy)}/default");
        var defaults = (await GetOkAsync(Templates)).EnumerateArray().Where(t => t.GetProperty("isDefault").GetBoolean()).Select(Id);
        Assert.Equal(new[] { Id(copy) }, defaults);
    }

    [Fact]
    public async Task Block_Create_Update_Move_Delete_RoundTrip()
    {
        var template = await CreateTemplateAsync();
        var created = await PostCreatedAsync($"{Templates}/{Id(template)}/blocks", new
        {
            title = Text.Of(160), startTime = "00:00:00", endTime = "23:59:59", category = "Sleep", color = "#ffffffff",
            location = Text.Of(120), allowOverlap = true, notifyAtStart = true, sortOrder = -1
        });
        AssertJsonEquivalent(created, (await GetOkAsync($"{Templates}/{Id(template)}/blocks")).EnumerateArray().Single());
        AssertJsonEquivalent(created, (await GetOkAsync($"{Templates}/{Id(template)}")).GetProperty("blocks").EnumerateArray().Single());

        var updated = await PutOkAsync($"{Blocks}/{Id(created)}", Block("Renamed", "08:00:00", "09:30:00"));
        AssertJsonEquivalent(updated, (await GetOkAsync($"{Templates}/{Id(template)}/blocks")).EnumerateArray().Single());

        var moved = await PutOkAsync($"{Blocks}/{Id(created)}/move", new { startTime = "10:00:00", endTime = "10:15:00" });
        Assert.Equal("10:00:00", Str(moved, "startTime"));
        AssertJsonEquivalent(moved, (await GetOkAsync($"{Templates}/{Id(template)}/blocks")).EnumerateArray().Single());

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Blocks}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Blocks}/{Id(created)}"));
    }

    [Fact]
    public async Task Block_Validation_Overlap_Fk_And404s()
    {
        var template = await CreateTemplateAsync();
        var url = $"{Templates}/{Id(template)}/blocks";
        var first = await PostCreatedAsync(url, Block("A", "09:00:00", "10:00:00"));

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, Block(" ", "11:00:00", "12:00:00")), "title");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, Block("Zero", "11:00:00", "11:00:00")));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, Block("Backwards", "12:00:00", "11:00:00")));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, Block(Text.Of(161), "11:00:00", "12:00:00")));
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, Block("Fk", "11:00:00", "12:00:00", checklistId: Guid.NewGuid())), "checklistId");

        var conflict = await Client.PostAsJsonAsync(url, Block("Overlap", "09:30:00", "10:30:00"));
        await AssertStatusAsync(HttpStatusCode.Conflict, conflict);
        Assert.Equal(Id(first), (await ReadJsonAsync(conflict)).GetProperty("conflictingBlockId").GetGuid());
        await PostCreatedAsync(url, Block("Touching", "10:00:00", "11:00:00"));
        await PostCreatedAsync(url, Block("Allowed overlap", "09:15:00", "09:45:00", allowOverlap: true));

        await AssertStatusAsync(HttpStatusCode.Conflict, await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}/move", new { startTime = "10:30:00", endTime = "10:45:00" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}/move", new { startTime = "10:30:00", endTime = "10:00:00" }));
        await AssertStatusAsync(HttpStatusCode.Conflict, await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}", Block("A", "10:30:00", "10:45:00")));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}", Block("A", "10:30:00", "10:00:00")));
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}", Block("", "09:00:00", "10:00:00")), "title");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}", Block("A", "09:00:00", "10:00:00", checklistId: Guid.NewGuid())), "checklistId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Blocks}/{Id(first)}", Block(Text.Of(161), "09:00:00", "10:00:00")));

        var unknown = Guid.NewGuid();
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{Blocks}/{unknown}", Block("x", "01:00:00", "02:00:00")));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{Blocks}/{unknown}/move", new { startTime = "01:00:00", endTime = "02:00:00" }));
    }

    [Fact]
    public async Task Block_Validate_ReportsConflicts()
    {
        var template = await CreateTemplateAsync();
        var block = await PostCreatedAsync($"{Templates}/{Id(template)}/blocks", Block("A", "09:00:00", "10:00:00"));

        var clash = await PostOkAsync($"{Blocks}/validate", new { templateId = Id(template), startTime = "09:30:00", endTime = "11:00:00", allowOverlap = false });
        Assert.False(clash.GetProperty("valid").GetBoolean());
        Assert.Equal(Id(block), Id(clash.GetProperty("conflicts").EnumerateArray().Single()));

        var self = await PostOkAsync($"{Blocks}/validate", new { templateId = Id(template), blockId = Id(block), startTime = "09:30:00", endTime = "11:00:00", allowOverlap = false });
        Assert.True(self.GetProperty("valid").GetBoolean());
        var allowed = await PostOkAsync($"{Blocks}/validate", new { templateId = Id(template), startTime = "09:30:00", endTime = "11:00:00", allowOverlap = true });
        Assert.True(allowed.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task Assignments_Create_List_Delete_AndFk()
    {
        var template = await CreateTemplateAsync();
        var weekday = await PostCreatedAsync(Assignments, new { templateId = Id(template), scope = "Weekday", dayOfWeek = "Saturday", priority = 2 });
        var range = await PostCreatedAsync(Assignments, new { templateId = Id(template), scope = "DateRange", dateFrom = "0001-01-01", dateTo = "9999-12-31", priority = int.MaxValue });

        var list = (await GetOkAsync(Assignments)).EnumerateArray().ToList();
        AssertJsonEquivalent(weekday, list.Single(a => Id(a) == Id(weekday)));
        AssertJsonEquivalent(range, list.Single(a => Id(a) == Id(range)));

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(Assignments, new { templateId = Guid.NewGuid(), scope = "Weekday", dayOfWeek = "Monday" }), "templateId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, Assignments, $"{{\"templateId\":\"{Id(template)}\",\"scope\":\"Monthly\"}}"));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Assignments}/{Id(weekday)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Assignments}/{Id(weekday)}"));
        Assert.Single((await GetOkAsync(Assignments)).EnumerateArray());
    }

    [Fact]
    public async Task Resolve_MostSpecificWins()
    {
        var def = await CreateTemplateAsync("Default");
        var weekdayT = await CreateTemplateAsync("Monday");
        var rangeT = await CreateTemplateAsync("Range");
        var specificT = await CreateTemplateAsync("Specific");
        var overrideT = await CreateTemplateAsync("Override");
        const string monday = "2026-10-05";

        async Task<(Guid? Id, string Reason)> ResolveAsync()
        {
            var r = await GetOkAsync($"/api/v1/timetable/resolve?date={monday}");
            var id = r.GetProperty("templateId");
            return (id.ValueKind == JsonValueKind.Null ? null : id.GetGuid(), Str(r, "reason"));
        }

        Assert.Equal((null, "none"), await ResolveAsync());
        await PatchOkAsync($"{Templates}/{Id(def)}/default");
        Assert.Equal((Id(def), "template:IsDefault"), await ResolveAsync());
        await PostCreatedAsync(Assignments, new { templateId = Id(weekdayT), scope = "Weekday", dayOfWeek = "Monday" });
        Assert.Equal((Id(weekdayT), "assignment:Weekday"), await ResolveAsync());
        await PostCreatedAsync(Assignments, new { templateId = Id(rangeT), scope = "DateRange", dateFrom = "2026-10-01", dateTo = "2026-10-31" });
        Assert.Equal((Id(rangeT), "assignment:DateRange"), await ResolveAsync());
        await PostCreatedAsync(Assignments, new { templateId = Id(specificT), scope = "SpecificDate", dateFrom = monday });
        Assert.Equal((Id(specificT), "assignment:SpecificDate"), await ResolveAsync());
        await PutOkAsync($"{Overrides}/{monday}", new { mode = "UseTemplate", templateId = Id(overrideT) });
        Assert.Equal((Id(overrideT), "day_override:UseTemplate"), await ResolveAsync());
        await PutOkAsync($"{Overrides}/{monday}", new { mode = "RestDay" });
        Assert.Equal((null, "day_override:RestDay"), await ResolveAsync());

        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/timetable/resolve"));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync("/api/v1/timetable/resolve?date=not-a-date"));
    }

    [Fact]
    public async Task Resolve_AndToday_AgreeWhenOverrideTemplateWasDeleted()
    {
        var def = await CreateTemplateAsync("Default");
        await PatchOkAsync($"{Templates}/{Id(def)}/default");
        var doomed = await CreateTemplateAsync("Doomed");
        var date = Today.ToString("yyyy-MM-dd");
        await PutOkAsync($"{Overrides}/{date}", new { mode = "UseTemplate", templateId = Id(doomed), note = "swap" });

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Templates}/{Id(doomed)}"));

        var resolved = await GetOkAsync($"/api/v1/timetable/resolve?date={date}");
        Assert.Equal(Id(def), resolved.GetProperty("templateId").GetGuid());
        var today = await GetOkAsync($"/api/v1/today?date={date}");
        Assert.Equal(Id(def), today.GetProperty("template").GetProperty("id").GetGuid());
        Assert.Equal("swap", Str(today.GetProperty("override"), "note"));
    }

    [Fact]
    public async Task DayOverrides_Upsert_Get_List_Delete()
    {
        var template = await CreateTemplateAsync();
        var created = await PutOkAsync($"{Overrides}/2026-12-25", new { mode = "UseTemplate", templateId = Id(template), note = Text.Of(200) });
        AssertJsonEquivalent(created, await GetOkAsync($"{Overrides}/2026-12-25"));

        var replaced = await PutOkAsync($"{Overrides}/2026-12-25", new { mode = "RestDay", note = "🎄" });
        Assert.Equal(Id(created), Id(replaced));
        AssertJsonEquivalent(replaced, (await GetOkAsync(Overrides)).EnumerateArray().Single());

        await PutOkAsync($"{Overrides}/0001-01-01", new { mode = "CustomOnly" });
        await PutOkAsync($"{Overrides}/9999-12-31", new { mode = "RestDay" });
        Assert.Equal(new[] { "0001-01-01", "2026-12-25", "9999-12-31" }, (await GetOkAsync(Overrides)).EnumerateArray().Select(o => Str(o, "date")));

        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Overrides}/2026-12-26", new { mode = "UseTemplate", templateId = Guid.NewGuid() }), "templateId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Overrides}/2026-12-26", new { mode = "RestDay", note = Text.Of(201) }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Overrides}/2026-13-40", new { mode = "RestDay" }));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Overrides}/2026-12-25"));
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Overrides}/2026-12-25"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{Overrides}/2026-12-25"));
    }

    private async Task<JsonElement> PostOkAsync(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadJsonAsync(response);
    }
}
