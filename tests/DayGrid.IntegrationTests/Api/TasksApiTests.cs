using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class TasksApiTests : IntegrationTestBase
{
    private const string Url = "/api/v1/tasks";

    public TasksApiTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<JsonElement> FindInListAsync(Guid id, string query = "") =>
        (await GetOkAsync(Url + query)).EnumerateArray().Single(t => Id(t) == id);

    [Fact]
    public async Task Create_ThenList_ReturnsIdenticalData()
    {
        var created = await PostCreatedAsync(Url, new { title = "  Buy milk  ", notes = "2 litres", priority = "High" });
        Assert.Equal("Buy milk", Str(created, "title"));
        Assert.Equal("Open", Str(created, "status"));
        Assert.Equal(0, created.GetProperty("sortOrder").GetInt32());

        AssertJsonEquivalent(created, await FindInListAsync(Id(created)));
    }

    [Fact]
    public async Task Create_AssignsIncreasingSortOrder()
    {
        var a = await PostCreatedAsync(Url, new { title = "a" });
        var b = await PostCreatedAsync(Url, new { title = "b" });
        Assert.Equal(a.GetProperty("sortOrder").GetInt32() + 1, b.GetProperty("sortOrder").GetInt32());
    }

    [Fact]
    public async Task Create_TitleAtMaxLength_Persists_AndUnicodeRoundTrips()
    {
        var bmp = await PostCreatedAsync(Url, new { title = Text.Of(200), notes = Text.Of(20_000) });
        Assert.Equal(Text.Of(200), Str(await FindInListAsync(Id(bmp)), "title"));

        // 200 emoji = 200 Postgres characters (400 UTF-16 units) — still fits varchar(200).
        var emoji = await PostCreatedAsync(Url, new { title = Text.Emoji(200) });
        Assert.Equal(Text.Emoji(200), Str(await FindInListAsync(Id(emoji)), "title"));
    }

    [Fact]
    public async Task Create_TitleOverMaxLength_Returns400_Not500()
    {
        var response = await Client.PostAsJsonAsync(Url, new { title = Text.Of(201) });
        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        Assert.Empty((await GetOkAsync(Url)).EnumerateArray());
    }

    [Fact]
    public async Task Create_TitleWithNulCharacter_Returns400_Not500()
    {
        var response = await Client.PostAsJsonAsync(Url, new { title = "bad\u0000title" });
        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
    }

    [Theory]
    [InlineData("{\"title\":null}")]
    [InlineData("{\"title\":\"\"}")]
    [InlineData("{\"title\":\"   \"}")]
    public async Task Create_BlankTitle_Returns400(string body)
    {
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Post, Url, body), "title");
    }

    [Theory]
    [InlineData("{\"notes\":\"no title\"}")]
    [InlineData("{not json")]
    [InlineData("{\"title\":\"x\",\"priority\":\"Bogus\"}")]
    public async Task Create_MalformedBody_Returns400(string body)
    {
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, Url, body));
    }

    [Fact]
    public async Task Update_Persists()
    {
        var created = await PostCreatedAsync(Url, new { title = "old" });
        var updated = await PutOkAsync($"{Url}/{Id(created)}", new { title = "new ✓", notes = "n", priority = "Critical" });

        Assert.Equal("new ✓", Str(updated, "title"));
        AssertJsonEquivalent(updated, await FindInListAsync(Id(created)));
        Assert.True(updated.GetProperty("updatedAt").GetDateTimeOffset() >= created.GetProperty("updatedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Update_Validation_And404()
    {
        var created = await PostCreatedAsync(Url, new { title = "x" });
        await AssertValidationErrorAsync(
            await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = " ", priority = "Low" }), "title");
        await AssertStatusAsync(HttpStatusCode.BadRequest,
            await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = Text.Of(201), priority = "Low" }));
        await AssertStatusAsync(HttpStatusCode.NotFound,
            await Client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { title = "x", priority = "Low" }));
    }

    [Fact]
    public async Task Status_DoneSetsCompletedAt_OpenClearsIt()
    {
        var created = await PostCreatedAsync(Url, new { title = "x" });

        var done = await PatchOkAsync($"{Url}/{Id(created)}/status", new { status = "Done" });
        Assert.Equal("Done", Str(done, "status"));
        Assert.Equal(JsonValueKind.String, done.GetProperty("completedAt").ValueKind);
        AssertJsonEquivalent(done, await FindInListAsync(Id(created)));

        var open = await PatchOkAsync($"{Url}/{Id(created)}/status", new { status = "Open" });
        Assert.Equal(JsonValueKind.Null, open.GetProperty("completedAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, (await FindInListAsync(Id(created))).GetProperty("completedAt").ValueKind);

        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsJsonAsync($"{Url}/{Guid.NewGuid()}/status", new { status = "Done" }));
    }

    [Fact]
    public async Task Delete_RemovesRow_ThenIs404()
    {
        var created = await PostCreatedAsync(Url, new { title = "x" });
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Url}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Url}/{Id(created)}"));
        Assert.Empty((await GetOkAsync(Url)).EnumerateArray());
    }

    [Fact]
    public async Task Reorder_PersistsAndIgnoresUnknownIds()
    {
        var a = await PostCreatedAsync(Url, new { title = "a" });
        var b = await PostCreatedAsync(Url, new { title = "b" });

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PutAsJsonAsync($"{Url}/reorder", new
        {
            items = new[] { new { id = Id(a), sortOrder = 10 }, new { id = Id(b), sortOrder = -3 }, new { id = Guid.NewGuid(), sortOrder = 1 } }
        }));

        var list = (await GetOkAsync(Url)).EnumerateArray().ToList();
        Assert.Equal(new[] { Id(b), Id(a) }, list.Select(Id));
        Assert.Equal(-3, list[0].GetProperty("sortOrder").GetInt32());

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PutAsJsonAsync($"{Url}/reorder", new { items = Array.Empty<object>() }));
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Put, $"{Url}/reorder", "{\"items\":null}"), "items");
    }

    [Fact]
    public async Task List_FiltersByStatus_AndSorts()
    {
        var low = await PostCreatedAsync(Url, new { title = "beta", priority = "Low" });
        var crit = await PostCreatedAsync(Url, new { title = "alpha", priority = "Critical" });
        await PatchOkAsync($"{Url}/{Id(low)}/status", new { status = "Done" });

        Assert.Equal(new[] { Id(crit) }, (await GetOkAsync($"{Url}?status=Open")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(low) }, (await GetOkAsync($"{Url}?status=Done")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(crit), Id(low) }, (await GetOkAsync($"{Url}?sort=title")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(crit), Id(low) }, (await GetOkAsync($"{Url}?sort=priority")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(crit), Id(low) }, (await GetOkAsync($"{Url}?sort=created")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(low), Id(crit) }, (await GetOkAsync(Url)).EnumerateArray().Select(Id));
    }

    [Fact]
    public async Task Search_IsCaseInsensitive_OnRealPostgresILike()
    {
        var milk = await PostCreatedAsync(Url, new { title = "Buy MILK today" });
        var umlaut = await PostCreatedAsync(Url, new { title = "Über-wichtig" });
        await PostCreatedAsync(Url, new { title = "Unrelated" });

        Assert.Equal(new[] { Id(milk) }, (await GetOkAsync($"{Url}?q=milk")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(umlaut) }, (await GetOkAsync($"{Url}?q={Uri.EscapeDataString("über")}")).EnumerateArray().Select(Id));
        Assert.Equal(3, (await GetOkAsync($"{Url}?q=%20%20")).GetArrayLength()); // whitespace = no filter
    }

    [Fact]
    public async Task Search_TreatsLikeWildcardsLiterally()
    {
        var percent = await PostCreatedAsync(Url, new { title = "100% done" });
        var underscore = await PostCreatedAsync(Url, new { title = "snake_case" });
        var backslash = await PostCreatedAsync(Url, new { title = @"C:\temp" });
        await PostCreatedAsync(Url, new { title = "1000 things" });

        Assert.Equal(new[] { Id(percent) }, (await GetOkAsync($"{Url}?q={Uri.EscapeDataString("100%")}")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(underscore) }, (await GetOkAsync($"{Url}?q=_")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(backslash) }, (await GetOkAsync($"{Url}?q={Uri.EscapeDataString(@":\")}")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(percent) }, (await GetOkAsync($"{Url}?q={Uri.EscapeDataString("%")}")).EnumerateArray().Select(Id));
    }
}
