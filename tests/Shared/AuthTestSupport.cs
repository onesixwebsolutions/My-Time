using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using DayGrid.Api.Auth;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.Extensions.DependencyInjection;

namespace DayGrid.TestSupport;

/// <summary>A <see cref="TimeProvider"/> frozen at one instant (drives the user clocks in tests).</summary>
public sealed class FrozenTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FrozenTimeProvider(DateTimeOffset now) => _utcNow = now.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

/// <summary>Captures outgoing email instead of sending it.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly List<(string To, string Subject, string Body)> _sent = new();

    public Exception? FailWith { get; set; }

    public IReadOnlyList<(string To, string Subject, string Body)> Sent
    {
        get { lock (_sent) return _sent.ToList(); }
    }

    public Task SendAsync(string toAddress, string subject, string htmlBody, CancellationToken ct = default)
    {
        if (FailWith is not null)
            throw FailWith;
        lock (_sent) _sent.Add((toAddress, subject, htmlBody));
        return Task.CompletedTask;
    }

    public IReadOnlyList<(string To, string Subject, string Body)> SentTo(string to) =>
        Sent.Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)).ToList();

    public void Reset()
    {
        FailWith = null;
        lock (_sent) _sent.Clear();
    }
}

/// <summary>
/// Adds <c>X-XSRF-TOKEN</c> (from the <c>XSRF-TOKEN</c> cookie) to every unsafe request, exactly
/// like Angular's HttpClient does — unless the request already has the header or opts out via
/// <see cref="TestSession.NoXsrf"/>.
/// </summary>
public sealed class XsrfHeaderHandler : DelegatingHandler
{
    private readonly CookieContainer _cookies;

    public XsrfHeaderHandler(CookieContainer cookies) => _cookies = cookies;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var method = request.Method;
        var unsafeMethod = method != HttpMethod.Get && method != HttpMethod.Head && method != HttpMethod.Options && method != HttpMethod.Trace;
        if (unsafeMethod
            && !request.Headers.Contains(AuthSupport.XsrfHeaderName)
            && !request.Options.TryGetValue(TestSession.NoXsrf, out _))
        {
            var token = _cookies.GetCookies(request.RequestUri!)[AuthSupport.XsrfCookieName]?.Value;
            if (token is not null)
                request.Headers.Add(AuthSupport.XsrfHeaderName, token);
        }
        return base.SendAsync(request, cancellationToken);
    }
}

/// <summary>An HTTP client with its own cookie jar (one browser session) against a test host.</summary>
public sealed class TestSession : IDisposable
{
    public static readonly HttpRequestOptionsKey<bool> NoXsrf = new("daygrid.no-xsrf");
    public static readonly Uri BaseAddress = new("https://localhost");

    public TestSession(WebApplicationFactory<Program> factory)
    {
        Cookies = new CookieContainer();
        Client = factory.CreateDefaultClient(BaseAddress, new XsrfHeaderHandler(Cookies), new CookieContainerHandler(Cookies));
    }

    public HttpClient Client { get; }
    public CookieContainer Cookies { get; }

    public string? Cookie(string name) => Cookies.GetCookies(BaseAddress)[name]?.Value;

    /// <summary>GET /auth/me — issues the XSRF-TOKEN cookie (what the SPA does at start-up).</summary>
    public Task<HttpResponseMessage> PrimeAsync() => Client.GetAsync("/api/v1/auth/me");

    public async Task<HttpResponseMessage> LoginAsync(string email, string password, bool rememberMe = false)
    {
        if (Cookie(AuthSupport.XsrfCookieName) is null)
            (await PrimeAsync()).Dispose();
        return await Client.PostAsJsonAsync("/api/v1/auth/login", new { email, password, rememberMe });
    }

    public async Task LoginOrThrowAsync(string email, string password)
    {
        using var response = await LoginAsync(email, password);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new InvalidOperationException($"Login failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
    }

    public Task<HttpResponseMessage> SendWithoutXsrfAsync(HttpRequestMessage request)
    {
        request.Options.Set(NoXsrf, true);
        return Client.SendAsync(request);
    }

    public void Dispose() => Client.Dispose();
}

public static class TestAccounts
{
    public const string Password = "correct horse battery";

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@example.test";

    /// <summary>Creates a confirmed account through the app's own AccountService (roles, settings row,
    /// first-account rules) without the email round trip. Returns the user id.</summary>
    public static async Task<Guid> CreateConfirmedAsync(IServiceProvider services, string email, string password = Password, string displayName = "Test User")
    {
        using var scope = services.CreateScope();
        var accounts = scope.ServiceProvider.GetRequiredService<AccountService>();
        var created = await accounts.CreateAsync(email, password, displayName, CancellationToken.None);
        if (!created.Result.Succeeded)
            throw new InvalidOperationException("Could not create test user: " + string.Join("; ", created.Result.Errors.Select(e => e.Description)));
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var user = (await userManager.FindByIdAsync(created.User.Id.ToString()))!;
        user.EmailConfirmed = true;
        await userManager.UpdateAsync(user);
        return user.Id;
    }

    /// <summary>A confirmed account plus a signed-in session for it.</summary>
    public static async Task<(TestSession Session, Guid UserId, string Email)> SignedInAsync(
        WebApplicationFactory<Program> factory, string prefix = "user", string password = Password)
    {
        var email = NewEmail(prefix);
        var userId = await CreateConfirmedAsync(factory.Services, email, password);
        var session = new TestSession(factory);
        await session.LoginOrThrowAsync(email, password);
        return (session, userId, email);
    }

    /// <summary>Extracts (first query parameter value, token) from the newest email link to <paramref name="path"/>.</summary>
    public static (string First, string Token) ExtractLink(FakeEmailSender email, string to, string path)
    {
        var message = email.SentTo(to).LastOrDefault(m => m.Body.Contains("/" + path + "?", StringComparison.Ordinal));
        if (message.Body is null)
            throw new InvalidOperationException($"No '{path}' email was sent to {to}. Sent: {string.Join(", ", email.Sent.Select(m => m.To + ": " + m.Subject))}");
        return ParseLink(WebUtility.HtmlDecode(message.Body), path);
    }

    public static (string First, string Token) ParseLink(string text, string path)
    {
        var match = Regex.Match(text, "/" + Regex.Escape(path) + @"\?(?:userId|email)=([^&""\s<]+)&token=([A-Za-z0-9_\-]+)");
        if (!match.Success)
            throw new InvalidOperationException($"No '{path}' link found in: {text}");
        return (Uri.UnescapeDataString(match.Groups[1].Value), match.Groups[2].Value);
    }

    public static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
