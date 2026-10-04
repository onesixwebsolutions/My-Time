using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using DayGrid.Infrastructure.Email;
using DayGrid.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Xunit;

namespace DayGrid.Api.Tests;

/// <summary>Authentication/authorization/CSRF/header behaviour of the HTTP pipeline (InMemory host).</summary>
public class SecurityPipelineTests : IClassFixture<DayGridApiFactory>
{
    private readonly DayGridApiFactory _factory;

    public SecurityPipelineTests(DayGridApiFactory factory) => _factory = factory;

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        var match = Regex.Match(text, "\"code\"\\s*:\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string ConcreteUrl(string pattern) =>
        Regex.Replace(pattern, @"\{([^}:=*]+)(:[^}]*)?\}", m => m.Groups[1].Value.ToLowerInvariant().Contains("date")
            ? "2026-10-05"
            : Guid.NewGuid().ToString()).Replace("{**path}", "whatever");

    [Fact]
    public async Task EveryProtectedApiEndpoint_Returns401ProblemForAnonymousCallers_NeverARedirect()
    {
        using var anonymous = new TestSession(_factory);
        (await anonymous.PrimeAsync()).Dispose(); // even with a valid anonymous XSRF token

        var checkedCount = 0;
        foreach (var endpoint in _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            if (!pattern.StartsWith("/api/", StringComparison.Ordinal) || endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
                continue;
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                var request = new HttpRequestMessage(new HttpMethod(method), ConcreteUrl(pattern) + "?date=2026-10-05&from=2026-10-01&to=2026-10-05");
                if (method is "POST" or "PUT" or "PATCH" or "DELETE")
                    request.Content = JsonContent.Create(new { });
                var response = await anonymous.Client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.Unauthorized, $"{method} {pattern}: expected 401, got {(int)response.StatusCode}");
                Assert.Null(response.Headers.Location);
                Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
                Assert.Equal("unauthenticated", await CodeAsync(response));
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 80, $"only {checkedCount} endpoints checked");
    }

    [Fact]
    public void OnlyTheContractsAnonymousEndpoints_AllowAnonymous()
    {
        var anonymous = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(e => (e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"]).Select(m => $"{m} {e.RoutePattern.RawText}"))
            .OrderBy(x => x)
            .ToList();

        Assert.Equal(new[]
        {
            "* /health/ready",
            "GET {*path:nonfile}",   // SPA fallback (index.html)
            "HEAD {*path:nonfile}",
            "GET /api/v1/auth/me",
            "GET /health",
            "POST /api/v1/auth/confirm-email",
            "POST /api/v1/auth/forgot-password",
            "POST /api/v1/auth/login",
            "POST /api/v1/auth/register",
            "POST /api/v1/auth/resend-confirmation",
            "POST /api/v1/auth/reset-password",
        }.OrderBy(x => x), anonymous);
    }

    [Fact]
    public async Task Health_IsAnonymous()
    {
        using var client = _factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task AnonymousMe_Is401_AndIssuesAReadableStrictXsrfCookie()
    {
        using var session = new TestSession(_factory);
        var response = await session.PrimeAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        // The antiforgery cookie proper is HttpOnly.
        Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("daygrid.af=", StringComparison.Ordinal) && c.Contains("httponly", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task UnsafeRequests_WithoutOrWithABadXsrfHeader_Get400Antiforgery()
    {
        var (session, _, email) = await TestAccounts.SignedInAsync(_factory);
        using (session)
        {
            var missing = await session.SendWithoutXsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks") { Content = JsonContent.Create(new { title = "x" }) });
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal("antiforgery", await CodeAsync(missing));

            var bad = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/tasks/{Guid.NewGuid()}");
            bad.Headers.Add("X-XSRF-TOKEN", "forged-token");
            Assert.Equal("antiforgery", await CodeAsync(await session.Client.SendAsync(bad)));

            // GET needs no token; with the header the same POST succeeds.
            Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/api/v1/tasks")).StatusCode);
            Assert.Equal(HttpStatusCode.Created, (await session.Client.PostAsJsonAsync("/api/v1/tasks", new { title = "x" })).StatusCode);
        }

        // Login and register are protected too.
        using var anonymous = new TestSession(_factory);
        (await anonymous.PrimeAsync()).Dispose();
        var login = await anonymous.SendWithoutXsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login")
        { Content = JsonContent.Create(new { email, password = TestAccounts.Password, rememberMe = false }) });
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);
        Assert.Equal("antiforgery", await CodeAsync(login));
        var register = await anonymous.SendWithoutXsrfAsync(new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/register")
        { Content = JsonContent.Create(new { email = TestAccounts.NewEmail(), password = TestAccounts.Password, displayName = "x" }) });
        Assert.Equal("antiforgery", await CodeAsync(register));
    }

    [Fact]
    public async Task XsrfToken_IsBoundToTheUser_AndReissuedOnLoginAndLogout()
    {
        var email = TestAccounts.NewEmail();
        await TestAccounts.CreateConfirmedAsync(_factory.Services, email);
        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();
        var anonymousToken = session.Cookie("XSRF-TOKEN");

        await session.LoginOrThrowAsync(email, TestAccounts.Password);
        var userToken = session.Cookie("XSRF-TOKEN");
        Assert.NotEqual(anonymousToken, userToken);

        // The pre-login token is no longer accepted for the signed-in user.
        var stale = new HttpRequestMessage(HttpMethod.Post, "/api/v1/tasks") { Content = JsonContent.Create(new { title = "x" }) };
        stale.Headers.Add("X-XSRF-TOKEN", anonymousToken);
        Assert.Equal("antiforgery", await CodeAsync(await session.Client.SendAsync(stale)));

        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.NotEqual(userToken, session.Cookie("XSRF-TOKEN"));
        // ...and the post-logout token works for the next login.
        Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(email, TestAccounts.Password)).StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_AreOnEveryResponse()
    {
        using var session = new TestSession(_factory);
        foreach (var response in new[] { await _factory.CreateClient().GetAsync("/health"), await session.PrimeAsync(), await session.Client.GetAsync("/api/v1/tasks") })
        {
            var h = response.Headers;
            var csp = Assert.Single(h.GetValues("Content-Security-Policy"));
            Assert.Contains("default-src 'self'", csp);
            Assert.Contains("script-src 'self'", csp);
            Assert.DoesNotContain("unsafe-eval", csp);
            Assert.Contains("frame-ancestors 'none'", csp);
            Assert.Equal("nosniff", Assert.Single(h.GetValues("X-Content-Type-Options")));
            Assert.Equal("DENY", Assert.Single(h.GetValues("X-Frame-Options")));
            Assert.Equal("strict-origin-when-cross-origin", Assert.Single(h.GetValues("Referrer-Policy")));
            Assert.Contains("camera=()", Assert.Single(h.GetValues("Permissions-Policy")));
        }
    }

    [Fact]
    public async Task Hsts_IsSentOverHttpsOutsideDevelopment()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://daygrid.test") });
        var response = await client.GetAsync("/health");
        Assert.Contains("max-age=", Assert.Single(response.Headers.GetValues("Strict-Transport-Security")));
    }

    [Fact]
    public async Task SignalRNegotiate_RequiresAuthentication()
    {
        using var anonymous = new TestSession(_factory);
        var denied = await anonymous.Client.PostAsync("/hubs/schedule/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Null(denied.Headers.Location);

        var (session, _, _) = await TestAccounts.SignedInAsync(_factory);
        using (session)
            Assert.Equal(HttpStatusCode.OK, (await session.Client.PostAsync("/hubs/schedule/negotiate?negotiateVersion=1", null)).StatusCode);
    }

    [Fact]
    public async Task RevokedSession_Gets401_AndAFreshXsrfTokenThatLetsTheUserSignInAgain()
    {
        var (session, _, email) = await TestAccounts.SignedInAsync(_factory);
        using (session)
        {
            using var other = new TestSession(_factory);
            await other.LoginOrThrowAsync(email, TestAccounts.Password);
            Assert.Equal(HttpStatusCode.NoContent, (await other.Client.PostAsJsonAsync("/api/v1/auth/change-password",
                new { currentPassword = TestAccounts.Password, newPassword = "a brand new passphrase" })).StatusCode);

            var revoked = await session.Client.GetAsync("/api/v1/tasks");
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await session.LoginAsync(email, "a brand new passphrase")).StatusCode);
        }
    }
}

/// <summary>Auth endpoints are limited per client IP (fixed window): 10 requests/minute -> 429.</summary>
public class RateLimitTests : IClassFixture<RateLimitTests.LimitedFactory>
{
    public sealed class LimitedFactory : DayGridApiFactory
    {
        protected override int AuthPermitLimit => 10;
    }

    private readonly LimitedFactory _factory;

    public RateLimitTests(LimitedFactory factory) => _factory = factory;

    [Fact]
    public async Task EleventhAuthRequestInAMinute_Gets429WithRetryAfter_ButMeAndOtherApisAreNotLimited()
    {
        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();

        for (var i = 0; i < 10; i++)
        {
            var response = await session.Client.PostAsJsonAsync("/api/v1/auth/login", new { email = "nobody@example.test", password = "whatever-123", rememberMe = false });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var limited = await session.Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "nobody@example.test" });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 60);
        Assert.Contains("\"code\":\"rate_limited\"", await limited.Content.ReadAsStringAsync());

        for (var i = 0; i < 15; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await session.Client.GetAsync("/api/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await session.Client.GetAsync("/health")).StatusCode);
    }
}

/// <summary>Email:Mode=Pickup end to end: the confirmation link is read from the .eml file on disk.</summary>
public class PickupEmailFlowTests : IClassFixture<PickupEmailFlowTests.PickupFactory>
{
    public sealed class PickupFactory : DayGridApiFactory
    {
        public string PickupDirectory { get; } = Path.Combine(Path.GetTempPath(), "daygrid-pickup-tests-" + Guid.NewGuid().ToString("N"));

        protected override void ConfigureSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Email:Mode", "Pickup");
            builder.UseSetting("Email:PickupDirectory", PickupDirectory);
            builder.UseSetting("Email:FromAddress", "no-reply@daygrid.test");
        }

        protected override void ReplaceEmailSender(IServiceCollection services)
        {
            // Keep the real IEmailSender selection (Pickup).
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (Directory.Exists(PickupDirectory))
                Directory.Delete(PickupDirectory, recursive: true);
        }
    }

    private readonly PickupFactory _factory;

    public PickupEmailFlowTests(PickupFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_ConfirmFromEmlFile_Login_Me()
    {
        Assert.IsType<PickupDirectoryEmailSender>(_factory.Services.CreateScope().ServiceProvider.GetRequiredService<IEmailSender>());

        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();
        var email = TestAccounts.NewEmail("pickup");
        Assert.Equal(HttpStatusCode.Accepted, (await session.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = TestAccounts.Password, displayName = "Pickup" })).StatusCode);

        // Sent by the background account-email queue.
        Assert.True(await _factory.Services.GetRequiredService<AccountEmailQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        var messages = new List<MimeMessage>();
        foreach (var file in Directory.GetFiles(_factory.PickupDirectory, "*.eml"))
            messages.Add(await MimeMessage.LoadAsync(file));
        var message = Assert.Single(messages, m => m.To.Mailboxes.Any(a => a.Address == email));
        Assert.Equal("Confirm your DayGrid account", message.Subject);
        Assert.Equal("no-reply@daygrid.test", message.From.Mailboxes.Single().Address);

        var (userId, token) = TestAccounts.ParseLink(WebUtility.HtmlDecode(message.HtmlBody!), "confirm-email");
        Assert.StartsWith("https://daygrid.test/confirm-email#userId=", WebUtility.HtmlDecode(Regex.Match(message.HtmlBody!, "href=\"([^\"]+)\"").Groups[1].Value));
        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token, password = TestAccounts.Password })).StatusCode);

        await session.LoginOrThrowAsync(email, TestAccounts.Password);
        var me = await TestAccounts.JsonAsync(await session.Client.GetAsync("/api/v1/auth/me"));
        Assert.Equal(email, me.GetProperty("email").GetString());
        Assert.True(me.GetProperty("emailConfirmed").GetBoolean());
    }
}
