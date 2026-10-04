using System.Net;
using System.Net.Http.Json;
using DayGrid.IntegrationTests.Infrastructure;
using DayGrid.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DayGrid.IntegrationTests.Auth;

/// <summary>Admin endpoints. The default test user is the first account and therefore Admin.</summary>
public class AdminTests : IntegrationTestBase
{
    private const string Admin = "/api/v1/admin";

    public AdminTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await TestAccounts.JsonAsync(response)).GetProperty("code").GetString()!;

    [Fact]
    public async Task FirstAccount_IsAdmin()
    {
        var me = await GetOkAsync("/api/v1/auth/me");
        Assert.Equal(new[] { "Admin", "User" }, me.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
    }

    [Fact]
    public async Task ListUsers_PagesSearchesAndReportsState()
    {
        var (bob, bobId, bobEmail) = await SignInAnotherUserAsync("bob");
        bob.Dispose();
        var (carol, carolId, _) = await SignInAnotherUserAsync("carol");
        carol.Dispose();

        var all = await GetOkAsync($"{Admin}/users");
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        var items = all.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { UserId, bobId, carolId }, items.Select(Id)); // oldest first
        var bobDto = items[1];
        Assert.Equal(bobEmail, Str(bobDto, "email"));
        Assert.Equal("Test User", Str(bobDto, "displayName"));
        Assert.Equal(new[] { "User" }, bobDto.GetProperty("roles").EnumerateArray().Select(r => r.GetString()));
        Assert.True(bobDto.GetProperty("emailConfirmed").GetBoolean());
        Assert.False(bobDto.GetProperty("lockedOut").GetBoolean());
        Assert.True(bobDto.TryGetProperty("createdAt", out _));
        Assert.NotEqual(System.Text.Json.JsonValueKind.Null, bobDto.GetProperty("lastLoginAt").ValueKind);

        var page2 = await GetOkAsync($"{Admin}/users?page=2&pageSize=2");
        Assert.Equal(3, page2.GetProperty("total").GetInt32());
        Assert.Equal(new[] { carolId }, page2.GetProperty("items").EnumerateArray().Select(Id));

        var search = await GetOkAsync($"{Admin}/users?search={Uri.EscapeDataString(bobEmail[..10].ToUpperInvariant())}");
        Assert.Equal(new[] { bobId }, search.GetProperty("items").EnumerateArray().Select(Id));
        Assert.Equal(1, search.GetProperty("total").GetInt32());

        // Never leaks credentials or tokens.
        var raw = await (await Client.GetAsync($"{Admin}/users")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NormalUser_Gets403_OnEveryAdminEndpoint_AndAnonymous401()
    {
        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            var calls = new Func<HttpClient, Task<HttpResponseMessage>>[]
            {
                c => c.GetAsync($"{Admin}/users"),
                c => c.PostAsync($"{Admin}/users/{UserId}/lock", null),
                c => c.PostAsync($"{Admin}/users/{UserId}/unlock", null),
                c => c.PostAsync($"{Admin}/users/{UserId}/resend-confirmation", null),
                c => c.DeleteAsync($"{Admin}/users/{UserId}")
            };
            using var anonymous = new TestSession(Fx.Factory);
            (await anonymous.PrimeAsync()).Dispose();
            foreach (var call in calls)
            {
                var forbidden = await call(bob.Client);
                Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
                Assert.Equal("forbidden", await CodeAsync(forbidden));
                Assert.Equal(HttpStatusCode.Unauthorized, (await call(anonymous.Client)).StatusCode);
            }
        }
        await using var db = NewSystemDb();
        Assert.Null((await db.Users.SingleAsync(u => u.Id == UserId)).LockoutEnd);
    }

    [Fact]
    public async Task Admin_CannotLockOrDeleteSelf()
    {
        var locked = await Client.PostAsync($"{Admin}/users/{UserId}/lock", null);
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Equal("cannot_lock_self", await CodeAsync(locked));
        var deleted = await Client.DeleteAsync($"{Admin}/users/{UserId}");
        Assert.Equal(HttpStatusCode.BadRequest, deleted.StatusCode);
        Assert.Equal("cannot_delete_self", await CodeAsync(deleted));

        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsync($"{Admin}/users/{Guid.NewGuid()}/lock", null));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Admin}/users/{Guid.NewGuid()}"));
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync("/api/v1/auth/me"));
    }

    [Fact]
    public async Task DeletingOwnAccount_AsTheOnlyAdmin_IsRefused_UntilAnotherAdminExists()
    {
        HttpRequestMessage DeleteMe(string password) =>
            new(HttpMethod.Delete, "/api/v1/auth/me") { Content = JsonContent.Create(new { password }) };

        // The password is still checked first.
        var wrong = await Client.SendAsync(DeleteMe("not my password"));
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal("invalid_credentials", await CodeAsync(wrong));

        var refused = await Client.SendAsync(DeleteMe(TestAccounts.Password));
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("last_admin", await CodeAsync(refused));
        await AssertStatusAsync(HttpStatusCode.OK, await Client.GetAsync("/api/v1/auth/me"));

        // A normal user (not an admin) existing does not help.
        var (bob, bobId, _) = await SignInAnotherUserAsync("bob");
        bob.Dispose();
        Assert.Equal("last_admin", await CodeAsync(await Client.SendAsync(DeleteMe(TestAccounts.Password))));

        // Once bob is an admin too, the first admin may leave.
        await using (var db = NewSystemDb())
        {
            db.UserRoles.Add(new Microsoft.AspNetCore.Identity.IdentityUserRole<Guid> { UserId = bobId, RoleId = DayGrid.Infrastructure.Identity.AppRoles.AdminRoleId });
            await db.SaveChangesAsync();
        }
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.SendAsync(DeleteMe(TestAccounts.Password)));
        await using (var db = NewSystemDb())
            Assert.False(await db.Users.AnyAsync(u => u.Id == UserId));
    }

    [Fact]
    public async Task Lock_EndsSessionsAndBlocksLogin_UnlockRestores()
    {
        var (bob, bobId, bobEmail) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            await AssertStatusAsync(HttpStatusCode.OK, await bob.Client.GetAsync("/api/v1/tasks"));
            await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PostAsync($"{Admin}/users/{bobId}/lock", null));

            await AssertStatusAsync(HttpStatusCode.Unauthorized, await bob.Client.GetAsync("/api/v1/tasks"));
            Assert.Equal("locked_out", await CodeAsync(await bob.LoginAsync(bobEmail, TestAccounts.Password)));
            var listed = (await GetOkAsync($"{Admin}/users")).GetProperty("items").EnumerateArray().Single(u => Id(u) == bobId);
            Assert.True(listed.GetProperty("lockedOut").GetBoolean());

            await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PostAsync($"{Admin}/users/{bobId}/unlock", null));
            await AssertStatusAsync(HttpStatusCode.OK, await bob.LoginAsync(bobEmail, TestAccounts.Password));
            await AssertStatusAsync(HttpStatusCode.OK, await bob.Client.GetAsync("/api/v1/tasks"));
        }
    }

    [Fact]
    public async Task Delete_RemovesTheUserAndAllTheirData_AndEndsTheirSession()
    {
        var (bob, bobId, bobEmail) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            await AuthFlowTests.SeedEverythingAsync(bob.Client);
            await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Admin}/users/{bobId}"));
            await AssertStatusAsync(HttpStatusCode.Unauthorized, await bob.Client.GetAsync("/api/v1/tasks"));
            Assert.Equal("invalid_credentials", await CodeAsync(await bob.LoginAsync(bobEmail, TestAccounts.Password)));
        }

        await using var db = NewSystemDb();
        foreach (var type in DayGrid.Infrastructure.Data.AppDbContext.UserOwnedTypes)
        {
            var table = db.Model.FindEntityType(type)!.GetTableName()!;
            var left = await db.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\" WHERE user_id = {0}", bobId).SingleAsync();
            Assert.True(left == 0, $"{left} row(s) left in {table}");
        }
        Assert.False(await db.Users.AnyAsync(u => u.Id == bobId));
    }

    [Fact]
    public async Task Admin_CannotReadAnotherUsersData()
    {
        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        Guid bobsChecklist;
        using (bob)
        {
            var created = await bob.Client.PostAsJsonAsync("/api/v1/checklists", new { name = "Bob's private list" });
            await AssertStatusAsync(HttpStatusCode.Created, created);
            bobsChecklist = Id(await TestAccounts.JsonAsync(created));
        }

        Assert.Empty((await GetOkAsync("/api/v1/checklists")).EnumerateArray());
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.GetAsync($"/api/v1/checklists/{bobsChecklist}"));
        var export = await (await Client.GetAsync("/api/v1/export")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("Bob's private list", export);
    }

    [Fact]
    public async Task ResendConfirmation_MailsOnlyUnconfirmedUsers()
    {
        var pending = TestAccounts.NewEmail("pending");
        using var anonymous = new TestSession(Fx.Factory);
        (await anonymous.PrimeAsync()).Dispose();
        await AssertStatusAsync(HttpStatusCode.Accepted, await anonymous.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = pending, password = TestAccounts.Password, displayName = "P" }));
        Guid pendingId;
        await using (var db = NewSystemDb())
            pendingId = (await db.Users.SingleAsync(u => u.Email == pending)).Id;
        Fx.Factory.Email.Reset();

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PostAsync($"{Admin}/users/{pendingId}/resend-confirmation", null));
        var (userId, _) = TestAccounts.ExtractLink(Fx.Factory.Email, pending, "confirm-email");
        Assert.Equal(pendingId, Guid.Parse(userId));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PostAsync($"{Admin}/users/{UserId}/resend-confirmation", null));
        Assert.Empty(Fx.Factory.Email.SentTo(UserEmail));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsync($"{Admin}/users/{Guid.NewGuid()}/resend-confirmation", null));
    }
}
