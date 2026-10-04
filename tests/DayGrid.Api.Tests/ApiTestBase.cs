using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DayGrid.Api.Tests;

public abstract class ApiTestBase : IClassFixture<DayGridApiFactory>
{
    protected readonly HttpClient Client;

    protected ApiTestBase(DayGridApiFactory factory)
    {
        Client = factory.CreateClient();
    }

    protected static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    protected Task<HttpResponseMessage> PostRawJsonAsync(string url, string json) =>
        Client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    protected Task<HttpResponseMessage> PutRawJsonAsync(string url, string json) =>
        Client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    protected async Task<JsonElement> PostCreatedAsync(string url, object body)
    {
        var response = await Client.PostAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    protected static async Task AssertValidationErrorAsync(HttpResponseMessage response, string field)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await ReadJsonAsync(response);
        Assert.True(json.GetProperty("errors").TryGetProperty(field, out _), $"expected a validation error for '{field}'");
    }
}
