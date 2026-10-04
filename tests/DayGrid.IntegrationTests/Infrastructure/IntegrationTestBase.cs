using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DayGrid.Infrastructure.Data;
using Xunit;

namespace DayGrid.IntegrationTests.Infrastructure;

/// <summary>Every test starts from an empty schema (plus the init.sql settings row).</summary>
[Collection(PostgresCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly PostgresFixture Fx;
    protected readonly HttpClient Client;

    protected IntegrationTestBase(PostgresFixture fixture)
    {
        Fx = fixture;
        Client = fixture.Factory.CreateClient();
    }

    public virtual async Task InitializeAsync()
    {
        await Fx.ResetAsync();
        Fx.Factory.Email.Reset();
    }

    public virtual Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    protected static DateOnly Today => DateOnly.FromDateTime(IntegrationApiFactory.FrozenNow.DateTime);

    protected AppDbContext NewDb() => Fx.CreateDbContext();

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    protected Task<HttpResponseMessage> SendJsonAsync(HttpMethod method, string url, string json) =>
        Client.SendAsync(new HttpRequestMessage(method, url) { Content = new StringContent(json, Encoding.UTF8, "application/json") });

    protected async Task<JsonElement> PostCreatedAsync(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body);
        await AssertStatusAsync(HttpStatusCode.Created, response);
        return await ReadJsonAsync(response);
    }

    protected async Task<JsonElement> GetOkAsync(string url)
    {
        var response = await Client.GetAsync(url);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadJsonAsync(response);
    }

    protected async Task<JsonElement> PutOkAsync(string url, object body)
    {
        var response = await Client.PutAsJsonAsync(url, body);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadJsonAsync(response);
    }

    protected async Task<JsonElement> PatchOkAsync(string url, object? body = null)
    {
        var response = body is null
            ? await Client.PatchAsync(url, null)
            : await Client.PatchAsJsonAsync(url, body);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return await ReadJsonAsync(response);
    }

    protected static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.Fail($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.PathAndQuery}: expected {(int)expected} {expected}, got {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
        }
    }

    protected static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        await AssertStatusAsync(HttpStatusCode.BadRequest, response);
        var json = await ReadJsonAsync(response);
        Assert.True(json.TryGetProperty("errors", out var errors) && errors.TryGetProperty(field, out _),
            $"expected a validation error for '{field}', got: {json}");
    }

    protected static Guid Id(JsonElement json) => json.GetProperty("id").GetGuid();

    protected static string Str(JsonElement json, string property) => json.GetProperty(property).GetString()!;

    /// <summary>
    /// Postgres timestamptz keeps microseconds; .NET ticks are 100ns. Compare instants at
    /// microsecond precision.
    /// </summary>
    protected static void AssertSameInstant(JsonElement expected, JsonElement actual)
    {
        var e = expected.GetDateTimeOffset();
        var a = actual.GetDateTimeOffset();
        Assert.True(Math.Abs((e - a).Ticks) < 10, $"instants differ: {e:O} vs {a:O}");
    }

    /// <summary>Asserts every property of <paramref name="expected"/> equals <paramref name="actual"/>'s (timestamps to the microsecond).</summary>
    protected static void AssertJsonEquivalent(JsonElement expected, JsonElement actual, params string[] ignore)
    {
        foreach (var prop in expected.EnumerateObject())
        {
            if (ignore.Contains(prop.Name))
                continue;
            Assert.True(actual.TryGetProperty(prop.Name, out var other), $"missing property '{prop.Name}' in {actual}");
            if (prop.Value.ValueKind == JsonValueKind.String && prop.Value.TryGetDateTimeOffset(out _)
                && prop.Value.GetString()!.Contains('T') && other.ValueKind == JsonValueKind.String)
            {
                AssertSameInstant(prop.Value, other);
                continue;
            }
            if (prop.Value.ValueKind == JsonValueKind.Number && other.ValueKind == JsonValueKind.Number)
            {
                Assert.True(prop.Value.GetDecimal() == other.GetDecimal(), $"'{prop.Name}': {prop.Value} vs {other}");
                continue;
            }
            Assert.True(JsonElementEquals(prop.Value, other), $"'{prop.Name}' differs: {prop.Value.GetRawText()} vs {other.GetRawText()}");
        }
    }

    private static bool JsonElementEquals(JsonElement a, JsonElement b)
    {
        if (a.ValueKind != b.ValueKind) return false;
        switch (a.ValueKind)
        {
            case JsonValueKind.Object:
                var aProps = a.EnumerateObject().ToList();
                if (aProps.Count != b.EnumerateObject().Count()) return false;
                return aProps.All(p => b.TryGetProperty(p.Name, out var bv) && JsonElementEquals(p.Value, bv));
            case JsonValueKind.Array:
                var aItems = a.EnumerateArray().ToList();
                var bItems = b.EnumerateArray().ToList();
                return aItems.Count == bItems.Count && aItems.Zip(bItems).All(x => JsonElementEquals(x.First, x.Second));
            case JsonValueKind.Number:
                return a.GetDecimal() == b.GetDecimal();
            case JsonValueKind.String:
                if (a.TryGetDateTimeOffset(out var ad) && b.TryGetDateTimeOffset(out var bd) && a.GetString()!.Contains('T'))
                    return Math.Abs((ad - bd).Ticks) < 10;
                return a.GetString() == b.GetString();
            default:
                return true; // True/False/Null already matched on ValueKind
        }
    }
}
