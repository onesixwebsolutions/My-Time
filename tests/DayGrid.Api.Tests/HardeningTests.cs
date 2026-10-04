using System.Net;
using System.Net.Http.Json;
using System.Text;
using DayGrid.Api.Auth;
using DayGrid.Infrastructure.Email;
using DayGrid.Infrastructure.Identity;
using DayGrid.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DayGrid.Api.Tests;

/// <summary>Bootstrap-Admin policy selection (pure).</summary>
public class BootstrapAdminPolicyTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "DayGrid";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static BootstrapAdminPolicy Policy(string environment, params (string Key, string Value)[] settings) =>
        BootstrapAdminPolicy.FromConfiguration(
            new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build(),
            new Env(environment), new UpperInvariantLookupNormalizer());

    [Theory]
    [InlineData("Production", "External", BootstrapAdminMode.None)]
    [InlineData("Staging", "External", BootstrapAdminMode.None)]
    [InlineData("Testing", "External", BootstrapAdminMode.None)]
    [InlineData("Development", "External", BootstrapAdminMode.FirstConfirmedUser)]
    [InlineData("Production", "Embedded", BootstrapAdminMode.FirstConfirmedUser)]
    public void WithoutAConfiguredEmail_OnlyDevelopmentAndEmbeddedFallBackToFirstConfirmedUser(string environment, string dbMode, BootstrapAdminMode expected)
    {
        Assert.Equal(expected, Policy(environment, ("Database:Mode", dbMode)).Mode);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ConfiguredEmail_AlwaysWins_AndMatchesNormalized(string environment)
    {
        var policy = Policy(environment, ("Auth:BootstrapAdminEmail", "  Owner@Example.TEST "), ("Database:Mode", "Embedded"));
        Assert.Equal(BootstrapAdminMode.ConfiguredEmail, policy.Mode);
        Assert.True(policy.Matches("OWNER@EXAMPLE.TEST"));
        Assert.False(policy.Matches("SOMEONE@EXAMPLE.TEST"));
        Assert.False(policy.Matches(null));
    }

    [Fact]
    public void None_NeverMatches() => Assert.False(Policy("Production").Matches("ANY@EXAMPLE.TEST"));

    [Theory]
    [InlineData("not a uri")]
    [InlineData("http://vault.example/keys/dp")]
    public void KeyVaultKeyUri_MustBeAnAbsoluteHttpsUri(string value)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyVaultKeyUri"] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => AuthSetup.KeyVaultKeyUri(config));
    }

    [Fact]
    public void KeyVaultKeyUri_UnsetIsNull_ValidIsParsed()
    {
        Assert.Null(AuthSetup.KeyVaultKeyUri(new ConfigurationBuilder().Build()));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["DataProtection:KeyVaultKeyUri"] = "https://v.vault.azure.net/keys/dp" }).Build();
        Assert.Equal(new Uri("https://v.vault.azure.net/keys/dp"), AuthSetup.KeyVaultKeyUri(config));
    }
}

/// <summary>Testing environment, no Auth:BootstrapAdminEmail: nobody becomes Admin automatically
/// (the Production behaviour) — not even the very first account to confirm.</summary>
public class NoBootstrapAdminTests : IClassFixture<DayGridApiFactory>
{
    private readonly DayGridApiFactory _factory;
    public NoBootstrapAdminTests(DayGridApiFactory factory) => _factory = factory;

    [Fact]
    public async Task FirstConfirmedAccount_IsNotAdmin()
    {
        Assert.Equal(BootstrapAdminMode.None, _factory.Services.GetRequiredService<BootstrapAdminPolicy>().Mode);
        var (session, _, _) = await TestAccounts.SignedInAsync(_factory, "first");
        using (session)
        {
            var me = await TestAccounts.JsonAsync(await session.Client.GetAsync("/api/v1/auth/me"));
            Assert.Equal(new[] { "User" }, me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
            Assert.Equal(HttpStatusCode.Forbidden, (await session.Client.GetAsync("/api/v1/admin/users")).StatusCode);
        }
    }
}

/// <summary>Auth:BootstrapAdminEmail configured: only that address becomes Admin, at confirmation.</summary>
public class ConfiguredBootstrapAdminTests : IClassFixture<ConfiguredBootstrapAdminTests.Factory>
{
    public const string OwnerEmail = "owner@daygrid.test";

    public sealed class Factory : DayGridApiFactory
    {
        protected override void ConfigureSettings(IWebHostBuilder builder) =>
            builder.UseSetting("Auth:BootstrapAdminEmail", OwnerEmail.ToUpperInvariant());
    }

    private readonly Factory _factory;
    public ConfiguredBootstrapAdminTests(Factory factory) => _factory = factory;

    private async Task<string[]> RolesAsync(Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        return (await users.GetRolesAsync((await users.FindByIdAsync(userId.ToString()))!)).OrderBy(r => r).ToArray();
    }

    [Fact]
    public async Task OnlyTheConfiguredAddress_BecomesAdmin_AndOnlyOnceConfirmed()
    {
        // Someone else registers and confirms first: still just a User.
        var squatter = await TestAccounts.CreateConfirmedAsync(_factory.Services, TestAccounts.NewEmail("squatter"));
        Assert.Equal(new[] { "User" }, await RolesAsync(squatter));

        // The owner registers: not Admin while unconfirmed.
        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();
        Assert.Equal(HttpStatusCode.Accepted, (await session.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email = OwnerEmail, password = TestAccounts.Password, displayName = "Owner" })).StatusCode);
        var (userId, token) = await TestAccounts.ExtractLinkAsync(_factory.Email, OwnerEmail, "confirm-email");
        Assert.Equal(new[] { "User" }, await RolesAsync(Guid.Parse(userId)));

        Assert.Equal(HttpStatusCode.NoContent, (await session.Client.PostAsJsonAsync("/api/v1/auth/confirm-email",
            new { userId, token, password = TestAccounts.Password })).StatusCode);
        Assert.Equal(new[] { "Admin", "User" }, await RolesAsync(Guid.Parse(userId)));
        Assert.Equal(new[] { "User" }, await RolesAsync(squatter));
    }
}

/// <summary>Request-size and collection caps (DoS), CORS registration and account-email flood protection.</summary>
public class LimitsTests : ApiTestBase
{
    public LimitsTests(DayGridApiFactory factory) : base(factory) { }

    [Theory]
    [InlineData("from=2026-10-01&to=2026-12-01", HttpStatusCode.OK)]          // 62 days inclusive
    [InlineData("from=2026-10-01&to=2026-12-02", HttpStatusCode.BadRequest)]  // 63 days
    public async Task DaysRange_IsCappedAt62Days(string query, HttpStatusCode expected)
    {
        Assert.Equal(expected, (await Client.GetAsync($"/api/v1/days/range?{query}")).StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/tasks/reorder")]
    [InlineData("/api/v1/checklists/reorder")]
    public async Task Reorder_RejectsMoreThan500Items(string url)
    {
        object Items(int n) => new { items = Enumerable.Range(0, n).Select(i => new { id = Guid.NewGuid(), sortOrder = i }).ToArray() };
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync(url, Items(501)), "items");
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PutAsJsonAsync(url, Items(500))).StatusCode);
    }

    [Fact]
    public async Task ChecklistItemReorder_RejectsMoreThan500Items()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        var body = new { items = Enumerable.Range(0, 501).Select(i => new { id = Guid.NewGuid(), sortOrder = i }).ToArray() };
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"/api/v1/checklists/{checklist.GetProperty("id").GetGuid()}/items/reorder", body), "items");
    }

    [Fact]
    public async Task FutureTask_RejectsMoreThan20RemindersPerRequest()
    {
        object Task(int n) => new
        {
            title = "Many", dueDate = "2026-12-01", priority = "Normal",
            reminders = Enumerable.Range(0, n).Select(i => new { offsetMinutes = i, channels = new[] { "InApp" } }).ToArray()
        };
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync("/api/v1/future-tasks", Task(21)), "reminders");
        var created = await PostCreatedAsync("/api/v1/future-tasks", Task(20));
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"/api/v1/future-tasks/{created.GetProperty("id").GetGuid()}", Task(21)), "reminders");
    }

    [Fact]
    public async Task RequestBodies_LargerThan1MB_Get413()
    {
        var huge = "{\"title\":\"" + new string('x', 1024 * 1024 + 10) + "\"}";
        var response = await Client.PostAsync("/api/v1/tasks", new StringContent(huge, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Contains("payload_too_large", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cors_OutsideDevelopment_NoPolicyUnlessConfigured()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/tasks");
        request.Headers.Add("Origin", "http://localhost:4200");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        var response = await Client.SendAsync(request);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(response.Headers.Contains("Access-Control-Allow-Credentials"));
    }
}

/// <summary>Cors:AllowedOrigins set explicitly: the policy exists outside Development too.</summary>
public class ConfiguredCorsTests : IClassFixture<ConfiguredCorsTests.Factory>
{
    public sealed class Factory : DayGridApiFactory
    {
        protected override void ConfigureSettings(IWebHostBuilder builder) =>
            builder.UseSetting("Cors:AllowedOrigins:0", "https://app.example.test");
    }

    private readonly Factory _factory;
    public ConfiguredCorsTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task ConfiguredOrigin_IsAllowed_OthersAreNot()
    {
        using var client = _factory.CreateClient();
        async Task<HttpResponseMessage> Preflight(string origin)
        {
            var request = new HttpRequestMessage(HttpMethod.Options, "/api/v1/tasks");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            return await client.SendAsync(request);
        }

        Assert.Equal("https://app.example.test", Assert.Single((await Preflight("https://app.example.test")).Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False((await Preflight("http://localhost:4200")).Headers.Contains("Access-Control-Allow-Origin"));
    }
}

/// <summary>Account emails: queued (never sent inline), throttled per recipient.</summary>
public class AccountEmailFloodTests : IClassFixture<AccountEmailFloodTests.Factory>
{
    public sealed class Factory : DayGridApiFactory
    {
        protected override void ConfigureSettings(IWebHostBuilder builder)
        {
            builder.UseSetting("Email:AccountEmails:CooldownSeconds", "60");
            builder.UseSetting("Email:AccountEmails:DailyLimitPerRecipient", "10");
        }
    }

    private readonly Factory _factory;
    public AccountEmailFloodTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task RepeatedRequests_SendOneEmailPerKindPerCooldown_AndAlwaysAnswer202()
    {
        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();
        var email = TestAccounts.NewEmail("flood");
        Assert.Equal(HttpStatusCode.Accepted, (await session.Client.PostAsJsonAsync("/api/v1/auth/register",
            new { email, password = TestAccounts.Password, displayName = "F" })).StatusCode);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await session.Client.PostAsJsonAsync("/api/v1/auth/resend-confirmation", new { email })).StatusCode);
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await session.Client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email })).StatusCode);

        await _factory.Email.SettleAsync();
        var sent = _factory.Email.SentTo(email);
        Assert.Equal(1, sent.Count(m => m.Subject == "Confirm your DayGrid account"));
        Assert.Equal(1, sent.Count(m => m.Subject == "Reset your DayGrid password"));
    }

    [Fact]
    public async Task AFailingMailServer_DoesNotChangeTheResponse()
    {
        using var session = new TestSession(_factory);
        (await session.PrimeAsync()).Dispose();
        var email = TestAccounts.NewEmail("smtpdown");
        _factory.Email.FailWith = new InvalidOperationException("SMTP down");
        try
        {
            var response = await session.Client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = TestAccounts.Password, displayName = "S" });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("{}", await response.Content.ReadAsStringAsync());
            Assert.True(await _factory.Services.GetRequiredService<AccountEmailQueue>().WaitForIdleAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            _factory.Email.FailWith = null;
        }
    }
}
