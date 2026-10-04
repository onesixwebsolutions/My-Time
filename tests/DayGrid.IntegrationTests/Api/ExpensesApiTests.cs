using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class ExpensesApiTests : IntegrationTestBase
{
    public ExpensesApiTests(PostgresFixture fixture) : base(fixture) { }

    public static TheoryData<string, string> DatedKinds => new()
    {
        { "/api/v1/expenses/varying", "title" },
        { "/api/v1/expenses/spends", "title" }
    };

    [Fact]
    public async Task Constant_CrudRoundTrip_ActiveToggle_AndBoundaries()
    {
        const string url = "/api/v1/expenses/constant";
        var created = await PostCreatedAsync(url, new { name = Text.Of(160), amount = 9_999_999_999.99m, category = " Housing ", dayOfMonth = 31, notes = Text.Emoji(100) });
        Assert.Equal("Housing", Str(created, "category"));
        AssertJsonEquivalent(created, await GetOkAsync($"{url}/{Id(created)}"));

        var updated = await PutOkAsync($"{url}/{Id(created)}", new { name = "Rent", amount = 0m, category = (string?)null, dayOfMonth = 1, notes = (string?)null });
        AssertJsonEquivalent(updated, await GetOkAsync($"{url}/{Id(created)}"));

        var inactive = await PatchOkAsync($"{url}/{Id(created)}/active?active=false");
        Assert.False(inactive.GetProperty("isActive").GetBoolean());
        Assert.Empty((await GetOkAsync(url)).EnumerateArray());
        AssertJsonEquivalent(inactive, (await GetOkAsync($"{url}?includeInactive=true")).EnumerateArray().Single());

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { name = " ", amount = 1m }), "name");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { name = "x", amount = -0.01m }), "amount");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { name = "x", amount = 1m, dayOfMonth = 0 }), "dayOfMonth");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { name = "x", amount = 1m, dayOfMonth = 32 }), "dayOfMonth");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{url}/{Id(created)}", new { name = "x", amount = -1m }), "amount");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{url}/{Id(created)}", new { name = "x", amount = 1m, dayOfMonth = 32 }), "dayOfMonth");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{url}/{Id(created)}", new { name = "", amount = 1m }), "name");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, new { name = Text.Of(161), amount = 1m }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, new { name = "x", amount = 10_000_000_000m }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, new { name = "x", amount = 1m, category = Text.Of(61) }));

        var unknown = Guid.NewGuid();
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{url}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{url}/{unknown}", new { name = "x", amount = 1m }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsync($"{url}/{unknown}/active?active=true", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{url}/{unknown}"));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{url}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{url}/{Id(created)}"));
    }

    [Theory]
    [MemberData(nameof(DatedKinds))]
    public async Task Dated_CrudRoundTrip_FiltersAndBoundaries(string url, string titleField)
    {
        var created = await PostCreatedAsync(url, new { title = Text.Of(160), amount = 12.5m, category = "Food", date = "2026-10-05", notes = Text.Emoji(50) });
        AssertJsonEquivalent(created, await GetOkAsync($"{url}/{Id(created)}"));
        var old = await PostCreatedAsync(url, new { title = "old", amount = 1m, category = "Travel", date = "0001-01-01" });
        var future = await PostCreatedAsync(url, new { title = "future", amount = 1m, date = "9999-12-31" });

        Assert.Equal(new[] { Id(future), Id(created), Id(old) }, (await GetOkAsync(url)).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(created) }, (await GetOkAsync($"{url}?from=2026-10-01&to=2026-10-31")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(old) }, (await GetOkAsync($"{url}?category=Travel")).EnumerateArray().Select(Id));

        var updated = await PutOkAsync($"{url}/{Id(created)}", new { title = "renamed", amount = 3m, category = (string?)null, date = "2026-10-06", notes = (string?)null });
        AssertJsonEquivalent(updated, await GetOkAsync($"{url}/{Id(created)}"));

        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { title = "", amount = 1m, date = "2026-10-05" }), titleField);
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(url, new { title = "x", amount = -1m, date = "2026-10-05" }), "amount");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{url}/{Id(created)}", new { title = " ", amount = 1m, date = "2026-10-05" }), titleField);
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{url}/{Id(created)}", new { title = "x", amount = -1m, date = "2026-10-05" }), "amount");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, new { title = Text.Of(161), amount = 1m, date = "2026-10-05" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(url, new { title = "x", amount = 99_999_999_999.99m, date = "2026-10-05" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, url, "{\"title\":\"x\",\"amount\":1,\"date\":\"yesterday\"}"));

        var unknown = Guid.NewGuid();
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{url}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{url}/{unknown}", new { title = "x", amount = 1m, date = "2026-10-05" }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{url}/{unknown}"));
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{url}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"{url}/{Id(created)}"));
    }

    [Theory]
    [InlineData("/api/v1/expenses/constant", "{\"name\":\"x\",\"amount\":12.345}")]
    [InlineData("/api/v1/expenses/varying", "{\"title\":\"x\",\"amount\":12.345,\"date\":\"2026-10-05\"}")]
    [InlineData("/api/v1/expenses/spends", "{\"title\":\"x\",\"amount\":12.345,\"date\":\"2026-10-05\"}")]
    public async Task Amount_WithExtraDecimals_ResponseMatchesWhatIsStored(string url, string body)
    {
        var response = await SendJsonAsync(HttpMethod.Post, url, body);
        await AssertStatusAsync(HttpStatusCode.Created, response);
        var created = await ReadJsonAsync(response);

        var stored = await GetOkAsync($"{url}/{Id(created)}");
        Assert.Equal(12.35m, stored.GetProperty("amount").GetDecimal());
        Assert.Equal(12.35m, created.GetProperty("amount").GetDecimal());
    }
}
