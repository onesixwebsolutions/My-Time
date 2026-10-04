using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.Infrastructure.Data;
using DayGrid.IntegrationTests.Infrastructure;
using DayGrid.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DayGrid.IntegrationTests.Auth;

/// <summary>
/// The account lifecycle end to end against real Postgres, through the real cookie +
/// antiforgery pipeline, reading confirmation/reset links from the captured emails.
/// (The default test user — the first account, hence Admin — already exists.)
/// </summary>
public class AuthFlowTests : IntegrationTestBase
{
    private const string Auth = "/api/v1/auth";
    private const string GoodPassword = "a perfectly long passphrase";

    public AuthFlowTests(PostgresFixture fixture) : base(fixture) { }

    private TestSession NewSession() => new(Fx.Factory);

    private static async Task<string> CodeAsync(HttpResponseMessage response)
    {
        var json = await TestAccounts.JsonAsync(response);
        return json.TryGetProperty("code", out var code) ? code.GetString()! : "(no code) " + json;
    }

    private async Task<(TestSession Session, string Email, Guid UserId)> RegisterAndConfirmAsync(string password = GoodPassword, string displayName = "Bob")
    {
        var session = NewSession();
        var email = TestAccounts.NewEmail("bob");
        (await session.PrimeAsync()).Dispose();
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/register", new { email, password, displayName }));
        var (userId, token) = TestAccounts.ExtractLink(Fx.Factory.Email, email, "confirm-email");
        await AssertStatusAsync(HttpStatusCode.NoContent, await session.Client.PostAsJsonAsync($"{Auth}/confirm-email", new { userId, token }));
        return (session, email, Guid.Parse(userId));
    }

    [Fact]
    public async Task FullFlow_Register_Unconfirmed_Confirm_Login_Me_Logout()
    {
        using var session = NewSession();
        var email = TestAccounts.NewEmail("flow");

        // App start: anonymous /me is 401 but hands out the XSRF-TOKEN cookie.
        var me = await session.PrimeAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(me));
        Assert.NotNull(session.Cookie("XSRF-TOKEN"));

        var register = await session.Client.PostAsJsonAsync($"{Auth}/register", new { email, password = GoodPassword, displayName = "  Flow User  " });
        await AssertStatusAsync(HttpStatusCode.Accepted, register);
        Assert.Equal("{}", (await TestAccounts.JsonAsync(register)).GetRawText());

        // Correct password but unconfirmed -> email_not_confirmed; wrong password -> invalid_credentials.
        var early = await session.LoginAsync(email, GoodPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, early.StatusCode);
        Assert.Equal("email_not_confirmed", await CodeAsync(early));
        var wrong = await session.LoginAsync(email, GoodPassword + "x");
        Assert.Equal("invalid_credentials", await CodeAsync(wrong));

        // Confirmation link: {PublicBaseUrl}/confirm-email?userId=..&token=<base64url>
        var mail = Assert.Single(Fx.Factory.Email.SentTo(email));
        Assert.Contains("https://daygrid.test/confirm-email?userId=", WebUtility.HtmlDecode(mail.Body));
        var (userId, token) = TestAccounts.ExtractLink(Fx.Factory.Email, email, "confirm-email");

        var badConfirm = await session.Client.PostAsJsonAsync($"{Auth}/confirm-email", new { userId, token = token[..^4] + "AAAA" });
        Assert.Equal(HttpStatusCode.BadRequest, badConfirm.StatusCode);
        Assert.Equal("invalid_token", await CodeAsync(badConfirm));
        Assert.Equal("invalid_token", await CodeAsync(await session.Client.PostAsJsonAsync($"{Auth}/confirm-email", new { userId = Guid.NewGuid(), token })));
        await AssertStatusAsync(HttpStatusCode.NoContent, await session.Client.PostAsJsonAsync($"{Auth}/confirm-email", new { userId, token }));

        var login = await session.LoginAsync(email, GoodPassword);
        await AssertStatusAsync(HttpStatusCode.OK, login);
        var dto = await TestAccounts.JsonAsync(login);
        Assert.Equal(Guid.Parse(userId), dto.GetProperty("id").GetGuid());
        Assert.Equal(email, Str(dto, "email"));
        Assert.Equal("Flow User", Str(dto, "displayName"));
        Assert.Equal("Asia/Kolkata", Str(dto, "timeZone"));
        Assert.Equal(new[] { "User" }, dto.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True(dto.GetProperty("emailConfirmed").GetBoolean());
        Assert.True(dto.TryGetProperty("createdAt", out _));

        // Cookie flags: HttpOnly, SameSite=Strict, Secure; session cookie without rememberMe.
        var setCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("daygrid.auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
        var xsrf = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("XSRF-TOKEN=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", xsrf, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", xsrf, StringComparison.OrdinalIgnoreCase);

        var meAgain = await session.Client.GetAsync($"{Auth}/me");
        await AssertStatusAsync(HttpStatusCode.OK, meAgain);
        AssertJsonEquivalent(dto, await TestAccounts.JsonAsync(meAgain));
        await AssertStatusAsync(HttpStatusCode.OK, await session.Client.GetAsync("/api/v1/tasks"));

        await AssertStatusAsync(HttpStatusCode.NoContent, await session.Client.PostAsync($"{Auth}/logout", null));
        await AssertStatusAsync(HttpStatusCode.Unauthorized, await session.Client.GetAsync($"{Auth}/me"));
        await AssertStatusAsync(HttpStatusCode.Unauthorized, await session.Client.GetAsync("/api/v1/tasks"));
    }

    [Fact]
    public async Task RememberMe_IssuesAPersistentFourteenDayCookie()
    {
        var (session, email, _) = await RegisterAndConfirmAsync();
        using (session)
        {
            var login = await session.LoginAsync(email, GoodPassword, rememberMe: true);
            await AssertStatusAsync(HttpStatusCode.OK, login);
            var setCookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("daygrid.auth=", StringComparison.Ordinal));
            var expires = DateTimeOffset.Parse(setCookie.Split(';').Single(p => p.Trim().StartsWith("expires=", StringComparison.OrdinalIgnoreCase)).Split('=')[1]);
            Assert.InRange(expires - DateTimeOffset.UtcNow, TimeSpan.FromDays(13.9), TimeSpan.FromDays(14.1));
        }
    }

    [Fact]
    public async Task Register_ExistingEmail_GivesTheSameResponse_AndMailsANotice()
    {
        using var session = NewSession();
        (await session.PrimeAsync()).Dispose();
        var before = await CountUsersAsync();

        foreach (var variant in new[] { UserEmail, UserEmail.ToUpperInvariant() })
        {
            var response = await session.Client.PostAsJsonAsync($"{Auth}/register", new { email = variant, password = GoodPassword, displayName = "Imposter" });
            await AssertStatusAsync(HttpStatusCode.Accepted, response);
            Assert.Equal("{}", (await TestAccounts.JsonAsync(response)).GetRawText());
        }

        Assert.Equal(before, await CountUsersAsync());
        var notices = Fx.Factory.Email.SentTo(UserEmail);
        Assert.Equal(2, notices.Count);
        Assert.All(notices, n => Assert.Equal("You already have a DayGrid account", n.Subject));
        Assert.DoesNotContain(notices, n => n.Body.Contains("confirm-email", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ResendConfirmation_And_ForgotPassword_AreAlways202()
    {
        using var session = NewSession();
        (await session.PrimeAsync()).Dispose();
        var unknown = TestAccounts.NewEmail("nobody");

        foreach (var body in new object[] { new { email = unknown }, new { email = "not-an-email" }, new { email = (string?)null } })
        {
            await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/resend-confirmation", body));
            await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/forgot-password", body));
        }
        Assert.Empty(Fx.Factory.Email.Sent);

        // Confirmed account: resend sends nothing, forgot-password sends a reset link.
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/resend-confirmation", new { email = UserEmail }));
        Assert.Empty(Fx.Factory.Email.Sent);
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/forgot-password", new { email = UserEmail }));
        var (linkEmail, _) = TestAccounts.ExtractLink(Fx.Factory.Email, UserEmail, "reset-password");
        Assert.Equal(UserEmail, linkEmail);

        // Unconfirmed account: resend sends a fresh confirmation link.
        var pending = TestAccounts.NewEmail("pending");
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/register", new { email = pending, password = GoodPassword, displayName = "P" }));
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/resend-confirmation", new { email = pending }));
        Assert.Equal(2, Fx.Factory.Email.SentTo(pending).Count(m => m.Body.Contains("confirm-email", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("short1234", "password")]                    // 9 characters
    [InlineData("", "password")]
    [InlineData(null, "password")]
    public async Task Register_PasswordPolicy(string? password, string field)
    {
        using var session = NewSession();
        (await session.PrimeAsync()).Dispose();
        var response = await session.Client.PostAsJsonAsync($"{Auth}/register", new { email = TestAccounts.NewEmail(), password, displayName = "X" });
        await AssertValidationErrorAsync(response, field);
        Assert.Equal("validation", await CodeAsync(response));
    }

    [Fact]
    public async Task Register_PasswordPolicy_LengthBoundaries_NoCompositionRules()
    {
        using var session = NewSession();
        (await session.PrimeAsync()).Dispose();

        await AssertValidationErrorAsync(await session.Client.PostAsJsonAsync($"{Auth}/register",
            new { email = TestAccounts.NewEmail(), password = new string('x', 129), displayName = "X" }), "password");

        foreach (var ok in new[] { "aaaaaaaaaa", "1234567890", new string('y', 128), "lower only passphrase" })
            await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/register",
                new { email = TestAccounts.NewEmail(), password = ok, displayName = "X" }));
    }

    [Fact]
    public async Task Register_ValidatesEmailAndDisplayName()
    {
        using var session = NewSession();
        (await session.PrimeAsync()).Dispose();

        var response = await session.Client.PostAsJsonAsync($"{Auth}/register", new { email = "not an email", password = "x", displayName = " " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await TestAccounts.JsonAsync(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
        Assert.True(errors.TryGetProperty("displayName", out _));

        await AssertValidationErrorAsync(await session.Client.PostAsJsonAsync($"{Auth}/register",
            new { email = TestAccounts.NewEmail(), password = GoodPassword, displayName = new string('d', 101) }), "displayName");
        await AssertValidationErrorAsync(await session.Client.PostAsJsonAsync($"{Auth}/register",
            new { email = new string('e', 250) + "@x.test", password = GoodPassword, displayName = "D" }), "email");
    }

    [Fact]
    public async Task Login_UnknownEmailAndWrongPassword_AreIndistinguishable()
    {
        using var session = NewSession();
        var unknown = await session.LoginAsync(TestAccounts.NewEmail("ghost"), GoodPassword);
        var wrong = await session.LoginAsync(UserEmail, "wrong password!!");
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal("invalid_credentials", await CodeAsync(unknown));
        Assert.Equal("invalid_credentials", await CodeAsync(wrong));
    }

    [Fact]
    public async Task Lockout_AfterFiveFailures_EvenWithTheRightPassword()
    {
        using var session = NewSession();
        var codes = new List<string>();
        for (var i = 0; i < 5; i++)
            codes.Add(await CodeAsync(await session.LoginAsync(UserEmail, "wrong password " + i)));

        Assert.Equal(new[] { "invalid_credentials", "invalid_credentials", "invalid_credentials", "invalid_credentials", "locked_out" }, codes);
        var locked = await session.LoginAsync(UserEmail, TestAccounts.Password);
        Assert.Equal(HttpStatusCode.Unauthorized, locked.StatusCode);
        Assert.Equal("locked_out", await CodeAsync(locked));

        await using var db = NewSystemDb();
        var user = await db.Users.SingleAsync(u => u.Id == UserId);
        Assert.InRange(user.LockoutEnd!.Value - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15.1));
    }

    [Fact]
    public async Task SuccessfulLogin_ResetsTheFailureCounter_AndRecordsLastLogin()
    {
        using var session = NewSession();
        for (var i = 0; i < 4; i++)
            (await session.LoginAsync(UserEmail, "nope nope nope")).Dispose();
        await session.LoginOrThrowAsync(UserEmail, TestAccounts.Password);

        await using var db = NewSystemDb();
        var user = await db.Users.SingleAsync(u => u.Id == UserId);
        Assert.Equal(0, user.AccessFailedCount);
        Assert.NotNull(user.LastLoginAt);
    }

    [Fact]
    public async Task ChangePassword_InvalidatesTheOldPassword_AndOtherSessions_ButKeepsThisOne()
    {
        using var otherDevice = NewSession();
        await otherDevice.LoginOrThrowAsync(UserEmail, TestAccounts.Password);

        var wrongCurrent = await Client.PostAsJsonAsync($"{Auth}/change-password", new { currentPassword = "not it at all", newPassword = GoodPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrongCurrent.StatusCode);
        Assert.Equal("invalid_credentials", await CodeAsync(wrongCurrent));
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync($"{Auth}/change-password", new { currentPassword = TestAccounts.Password, newPassword = "short" }), "newPassword");

        await AssertStatusAsync(HttpStatusCode.NoContent,
            await Client.PostAsJsonAsync($"{Auth}/change-password", new { currentPassword = TestAccounts.Password, newPassword = GoodPassword }));

        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync($"{Auth}/me"));             // cookie re-issued
        await AssertStatusAsync(HttpStatusCode.Created, await Client.PostAsJsonAsync("/api/v1/tasks", new { title = "still here" })); // XSRF re-issued
        await AssertStatusAsync(HttpStatusCode.Unauthorized, await otherDevice.Client.GetAsync("/api/v1/tasks"));

        using var fresh = NewSession();
        Assert.Equal("invalid_credentials", await CodeAsync(await fresh.LoginAsync(UserEmail, TestAccounts.Password)));
        await AssertStatusAsync(HttpStatusCode.OK, await fresh.LoginAsync(UserEmail, GoodPassword));
        Assert.Contains(Fx.Factory.Email.SentTo(UserEmail), m => m.Subject == "Your DayGrid password was changed");
    }

    [Fact]
    public async Task ResetPassword_WithEmailedToken_InvalidatesOldPasswordSessionsAndTheToken()
    {
        using var anonymous = NewSession();
        (await anonymous.PrimeAsync()).Dispose();
        await AssertStatusAsync(HttpStatusCode.Accepted, await anonymous.Client.PostAsJsonAsync($"{Auth}/forgot-password", new { email = UserEmail }));
        var (email, token) = TestAccounts.ExtractLink(Fx.Factory.Email, UserEmail, "reset-password");

        var bad = await anonymous.Client.PostAsJsonAsync($"{Auth}/reset-password", new { email, token = "bm90LWEtdG9rZW4", newPassword = GoodPassword });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("invalid_token", await CodeAsync(bad));
        Assert.Equal("invalid_token", await CodeAsync(await anonymous.Client.PostAsJsonAsync($"{Auth}/reset-password",
            new { email = TestAccounts.NewEmail(), token, newPassword = GoodPassword })));
        await AssertValidationErrorAsync(await anonymous.Client.PostAsJsonAsync($"{Auth}/reset-password", new { email, token, newPassword = "short" }), "newPassword");

        await AssertStatusAsync(HttpStatusCode.NoContent, await anonymous.Client.PostAsJsonAsync($"{Auth}/reset-password", new { email, token, newPassword = GoodPassword }));

        // Reusing the token fails; the old password and the old session are dead.
        Assert.Equal("invalid_token", await CodeAsync(await anonymous.Client.PostAsJsonAsync($"{Auth}/reset-password",
            new { email, token, newPassword = "yet another passphrase" })));
        await AssertStatusAsync(HttpStatusCode.Unauthorized, await Client.GetAsync("/api/v1/tasks"));
        using var fresh = NewSession();
        Assert.Equal("invalid_credentials", await CodeAsync(await fresh.LoginAsync(UserEmail, TestAccounts.Password)));
        await AssertStatusAsync(HttpStatusCode.OK, await fresh.LoginAsync(UserEmail, GoodPassword));
    }

    [Fact]
    public async Task ResetPassword_EndsALockout()
    {
        using var session = NewSession();
        for (var i = 0; i < 5; i++)
            (await session.LoginAsync(UserEmail, "wrong wrong wrong")).Dispose();
        Assert.Equal("locked_out", await CodeAsync(await session.LoginAsync(UserEmail, TestAccounts.Password)));

        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync($"{Auth}/forgot-password", new { email = UserEmail }));
        var (email, token) = TestAccounts.ExtractLink(Fx.Factory.Email, UserEmail, "reset-password");
        await AssertStatusAsync(HttpStatusCode.NoContent, await session.Client.PostAsJsonAsync($"{Auth}/reset-password", new { email, token, newPassword = GoodPassword }));
        await AssertStatusAsync(HttpStatusCode.OK, await session.LoginAsync(UserEmail, GoodPassword));
    }

    [Fact]
    public async Task UpdateProfile_ValidatesTimeZone_AndTheClockFollowsIt()
    {
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Auth}/me", new { displayName = "Me", timeZone = "Nowhere/Land" }), "timeZone");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Auth}/me", new { displayName = "", timeZone = "UTC" }), "displayName");

        // Frozen now = 2026-10-05 05:00 UTC: still 2026-10-04 in Pago Pago (UTC-11).
        Assert.Equal("2026-10-05", Str(await GetOkAsync("/api/v1/today"), "date"));
        var updated = await PutOkAsync($"{Auth}/me", new { displayName = "Renamed", timeZone = "Pacific/Pago_Pago" });
        Assert.Equal("Renamed", Str(updated, "displayName"));
        Assert.Equal("Pacific/Pago_Pago", Str(updated, "timeZone"));
        Assert.Equal("Pacific/Pago_Pago", Str(await GetOkAsync("/api/v1/settings"), "timeZone"));
        Assert.Equal("2026-10-04", Str(await GetOkAsync("/api/v1/today"), "date"));

        // Another user's clock is unaffected.
        var (other, _, _) = await SignInAnotherUserAsync();
        using (other)
            Assert.Equal("2026-10-05", Str(await TestAccounts.JsonAsync(await other.Client.GetAsync("/api/v1/today")), "date"));
    }

    [Fact]
    public async Task FutureTaskReminders_UseTheUsersTimeZone()
    {
        await PutOkAsync($"{Auth}/me", new { displayName = "NY", timeZone = "America/New_York" });
        var task = await PostCreatedAsync("/api/v1/future-tasks", new
        {
            title = "Call", dueDate = "2026-12-01", dueTime = "09:00:00",
            reminders = new[] { new { offsetMinutes = 0, channels = new[] { "InApp" } } }
        });
        var fireAt = task.GetProperty("reminders")[0].GetProperty("fireAtUtc").GetDateTimeOffset();
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero), fireAt); // 09:00 EST = 14:00 UTC
    }

    [Fact]
    public async Task DeleteAccount_RequiresThePassword_AndRemovesEveryOwnedRow()
    {
        var (session, email, userId) = await RegisterAndConfirmAsync();
        using (session)
        {
            await session.LoginOrThrowAsync(email, GoodPassword);
            await SeedEverythingAsync(session.Client);
            await AssertOwnsRowsInEveryTableAsync(userId);

            var wrong = await session.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Auth}/me") { Content = JsonContent.Create(new { password = "not my password" }) });
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
            Assert.Equal("invalid_credentials", await CodeAsync(wrong));

            await AssertStatusAsync(HttpStatusCode.NoContent,
                await session.Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"{Auth}/me") { Content = JsonContent.Create(new { password = GoodPassword }) }));

            await AssertStatusAsync(HttpStatusCode.Unauthorized, await session.Client.GetAsync($"{Auth}/me"));
            Assert.Equal("invalid_credentials", await CodeAsync(await session.LoginAsync(email, GoodPassword)));
        }

        await AssertNoRowsLeftAsync(userId);
        // The default user's data is untouched.
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync("/api/v1/settings"));
    }

    // ------------------------------------------------------------------ helpers shared with AdminTests

    internal static async Task SeedEverythingAsync(HttpClient client)
    {
        async Task<JsonElement> Created(string url, object body)
        {
            var response = await client.PostAsJsonAsync(url, body);
            await AssertStatusAsync(HttpStatusCode.Created, response);
            return await TestAccounts.JsonAsync(response);
        }

        var checklist = await Created("/api/v1/checklists", new { name = "C" });
        var template = await Created("/api/v1/timetable/templates", new { name = "T", dayStart = "06:00:00", dayEnd = "22:00:00", slotMinutes = 30 });
        var block = await Created($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "B", startTime = "08:00:00", endTime = "09:00:00", category = "Work", checklistId = Id(checklist), allowOverlap = false, notifyAtStart = false, sortOrder = 0 });
        var item = await Created($"/api/v1/checklists/{Id(checklist)}/items", new { title = "I", anchorType = "LinkedToBlock", timetableBlockId = Id(block) });
        await AssertStatusAsync(HttpStatusCode.OK, await client.PostAsJsonAsync($"/api/v1/items/{Id(item)}/complete", new { date = "2026-10-05" }));
        await Created("/api/v1/timetable/assignments", new { templateId = Id(template), scope = "Weekday", dayOfWeek = "Monday", priority = 0 });
        await AssertStatusAsync(HttpStatusCode.OK, await client.PutAsJsonAsync("/api/v1/day-overrides/2026-10-06", new { mode = "UseTemplate", templateId = Id(template) }));
        await Created("/api/v1/future-tasks", new { title = "F", dueDate = "2026-12-01", promoteToChecklistId = Id(checklist), reminders = new[] { new { offsetMinutes = 5, channels = new[] { "InApp" } } } });
        await AssertStatusAsync(HttpStatusCode.OK, await client.PostAsync("/api/v1/notifications/test", null));
        await Created("/api/v1/tasks", new { title = "S" });
        await Created("/api/v1/expenses/constant", new { name = "Rent", amount = 10m });
        await Created("/api/v1/expenses/varying", new { title = "V", amount = 1m, date = "2026-10-01" });
        await Created("/api/v1/expenses/spends", new { title = "Sp", amount = 2m, date = "2026-10-02" });
        await AssertStatusAsync(HttpStatusCode.OK, await client.GetAsync("/api/v1/settings"));
    }

    private async Task<Dictionary<string, int>> CountOwnedRowsAsync(Guid userId)
    {
        await using var db = NewSystemDb();
        var counts = new Dictionary<string, int>();
        foreach (var type in AppDbContext.UserOwnedTypes)
        {
            var table = db.Model.FindEntityType(type)!.GetTableName()!;
            counts[table] = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\" WHERE user_id = {0}", userId).SingleAsync();
        }
        return counts;
    }

    internal async Task AssertOwnsRowsInEveryTableAsync(Guid userId)
    {
        var counts = await CountOwnedRowsAsync(userId);
        Assert.All(counts, c => Assert.True(c.Value > 0, $"seed did not create a row in {c.Key}"));
    }

    internal async Task AssertNoRowsLeftAsync(Guid userId)
    {
        var counts = await CountOwnedRowsAsync(userId);
        Assert.All(counts, c => Assert.True(c.Value == 0, $"{c.Value} row(s) of the deleted user left in {c.Key}"));
        await using var db = NewSystemDb();
        Assert.False(await db.Users.AnyAsync(u => u.Id == userId));
        Assert.False(await db.UserRoles.AnyAsync(r => r.UserId == userId));
    }

    private async Task<int> CountUsersAsync()
    {
        await using var db = NewSystemDb();
        return await db.Users.CountAsync();
    }
}
