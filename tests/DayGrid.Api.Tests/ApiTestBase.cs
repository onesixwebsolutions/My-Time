using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DayGrid.TestSupport;
using Xunit;

namespace DayGrid.Api.Tests;

/// <summary>
/// Each test runs as a brand-new, signed-in, email-confirmed user (real cookie + antiforgery
/// pipeline), so tests in one class never see each other's data.
/// </summary>
public abstract class ApiTestBase : IClassFixture<DayGridApiFactory>, IAsyncLifetime
{
    protected readonly DayGridApiFactory Factory;
    private TestSession? _session;

    protected ApiTestBase(DayGridApiFactory factory)
    {
        Factory = factory;
    }

    protected TestSession Session => _session ?? throw new InvalidOperationException("Not initialized.");
    protected HttpClient Client => Session.Client;
    protected Guid UserId { get; private set; }
    protected string UserEmail { get; private set; } = string.Empty;

    public virtual async Task InitializeAsync()
    {
        (_session, UserId, UserEmail) = await TestAccounts.SignedInAsync(Factory);
    }

    public virtual Task DisposeAsync()
    {
        _session?.Dispose();
        return Task.CompletedTask;
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
