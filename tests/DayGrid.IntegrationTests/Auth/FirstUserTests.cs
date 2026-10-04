using System.Net;
using System.Net.Http.Json;
using DayGrid.Domain.Entities;
using DayGrid.Infrastructure.Data;
using DayGrid.Infrastructure.Identity;
using DayGrid.IntegrationTests.Infrastructure;
using DayGrid.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests.Auth;

/// <summary>First-account rules: claims all legacy (unowned) rows and becomes Admin — race-safe.</summary>
public class FirstUserTests : IntegrationTestBase
{
    public FirstUserTests(PostgresFixture fixture) : base(fixture) { }

    protected override bool SignInDefaultUser => false;

    private static async Task<(TestSession Session, Guid UserId)> RegisterConfirmAndLoginAsync(IntegrationApiFactory factory, string email, string password = TestAccounts.Password)
    {
        var session = new TestSession(factory);
        (await session.PrimeAsync()).Dispose();
        await AssertStatusAsync(HttpStatusCode.Accepted, await session.Client.PostAsJsonAsync("/api/v1/auth/register", new { email, password, displayName = "Owner" }));
        var (userId, token) = TestAccounts.ExtractLink(factory.Email, email, "confirm-email");
        await AssertStatusAsync(HttpStatusCode.NoContent, await session.Client.PostAsJsonAsync("/api/v1/auth/confirm-email", new { userId, token }));
        await session.LoginOrThrowAsync(email, password);
        return (session, Guid.Parse(userId));
    }

    private static async Task<string[]> RolesAsync(TestSession session) =>
        (await TestAccounts.JsonAsync(await session.Client.GetAsync("/api/v1/auth/me")))
            .GetProperty("roles").EnumerateArray().Select(r => r.GetString()!).ToArray();

    [Fact]
    public async Task FirstAccount_ClaimsLegacyRowsAndIsAdmin_LaterAccountsStartEmpty()
    {
        Guid legacyChecklist;
        await using (var sys = NewSystemDb())
        {
            var checklist = new Checklist { Name = "Legacy list" };
            sys.Checklists.Add(checklist);
            sys.ChecklistItems.Add(new ChecklistItem { ChecklistId = checklist.Id, Title = "Legacy item" });
            sys.SimpleTasks.Add(new SimpleTask { Title = "Legacy task" });
            sys.AppSettings.Add(new AppSetting { TimeZone = "Europe/London", Theme = "dark" });
            await sys.SaveChangesAsync();
            legacyChecklist = checklist.Id;
        }

        var (owner, ownerId) = await RegisterConfirmAndLoginAsync(Fx.Factory, TestAccounts.NewEmail("owner"));
        using (owner)
        {
            Assert.Equal(new[] { "Admin", "User" }, await RolesAsync(owner));
            var lists = await TestAccounts.JsonAsync(await owner.Client.GetAsync("/api/v1/checklists"));
            Assert.Equal(legacyChecklist, Id(lists.EnumerateArray().Single()));
            // The legacy settings row became the owner's (no second row was created).
            var settings = await TestAccounts.JsonAsync(await owner.Client.GetAsync("/api/v1/settings"));
            Assert.Equal("Europe/London", Str(settings, "timeZone"));
            Assert.Equal("dark", Str(settings, "theme"));
        }

        await using (var sys = NewSystemDb())
        {
            foreach (var type in AppDbContext.UserOwnedTypes)
            {
                var table = sys.Model.FindEntityType(type)!.GetTableName()!;
                var unowned = await sys.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\" WHERE user_id IS NULL").SingleAsync();
                Assert.True(unowned == 0, $"{unowned} unowned row(s) left in {table}");
            }
            Assert.Equal(3, await sys.ChecklistItems.CountAsync(i => i.UserId == ownerId) + await sys.Checklists.CountAsync(c => c.UserId == ownerId) + await sys.SimpleTasks.CountAsync(t => t.UserId == ownerId));
            Assert.Equal(1, await sys.AppSettings.CountAsync(s => s.UserId == ownerId));
        }

        var (second, secondId) = await RegisterConfirmAndLoginAsync(Fx.Factory, TestAccounts.NewEmail("second"));
        using (second)
        {
            Assert.Equal(new[] { "User" }, await RolesAsync(second));
            Assert.Empty((await TestAccounts.JsonAsync(await second.Client.GetAsync("/api/v1/checklists"))).EnumerateArray());
            Assert.Empty((await TestAccounts.JsonAsync(await second.Client.GetAsync("/api/v1/tasks"))).EnumerateArray());
            var settings = await TestAccounts.JsonAsync(await second.Client.GetAsync("/api/v1/settings"));
            Assert.Equal("Asia/Kolkata", Str(settings, "timeZone")); // a fresh default row
        }
        await using (var sys = NewSystemDb())
            Assert.Equal(1, await sys.AppSettings.CountAsync(s => s.UserId == secondId));
    }

    [Fact]
    public async Task ConcurrentFirstRegistrations_ProduceExactlyOneAdmin()
    {
        const int n = 8;
        var sessions = Enumerable.Range(0, n).Select(_ => new TestSession(Fx.Factory)).ToList();
        try
        {
            foreach (var s in sessions)
                (await s.PrimeAsync()).Dispose();

            using var start = new SemaphoreSlim(0, n);
            var tasks = sessions.Select((s, i) => Task.Run(async () =>
            {
                await start.WaitAsync();
                return await s.Client.PostAsJsonAsync("/api/v1/auth/register", new { email = $"racer{i}-{Guid.NewGuid():N}@example.test", password = TestAccounts.Password, displayName = "R" });
            })).ToList();
            start.Release(n);
            var responses = await Task.WhenAll(tasks);
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        }
        finally
        {
            sessions.ForEach(s => s.Dispose());
        }

        await using var sys = NewSystemDb();
        Assert.Equal(n, await sys.Users.CountAsync());
        Assert.Equal(1, await sys.UserRoles.CountAsync(r => r.RoleId == AppRoles.AdminRoleId));
        Assert.Equal(n, await sys.UserRoles.CountAsync(r => r.RoleId == AppRoles.UserRoleId));
        Assert.Equal(n, await sys.AppSettings.CountAsync(s => s.UserId != null));
    }

    [Fact]
    public async Task ConcurrentRegistrations_OfOneEmail_CreateOneAccount()
    {
        var email = TestAccounts.NewEmail("dup");
        var sessions = Enumerable.Range(0, 4).Select(_ => new TestSession(Fx.Factory)).ToList();
        try
        {
            foreach (var s in sessions)
                (await s.PrimeAsync()).Dispose();
            var responses = await Task.WhenAll(sessions.Select(s =>
                s.Client.PostAsJsonAsync("/api/v1/auth/register", new { email, password = TestAccounts.Password, displayName = "D" })));
            Assert.All(responses, r => Assert.Equal(HttpStatusCode.Accepted, r.StatusCode));
        }
        finally
        {
            sessions.ForEach(s => s.Dispose());
        }

        await using var sys = NewSystemDb();
        Assert.Equal(1, await sys.Users.CountAsync());
    }

    /// <summary>
    /// The owner's real upgrade path: a database built from the old db/init.sql plus sample data,
    /// then migrated, then the first registration claims everything and becomes Admin.
    /// </summary>
    [Fact]
    public async Task LegacyInitSqlDatabase_UpgradesCleanly_AndTheFirstAccountOwnsEverything()
    {
        var cs = await Fx.CreateEmptyDatabaseAsync("upgrade");
        try
        {
            await using (var connection = new NpgsqlConnection(cs))
            {
                await connection.OpenAsync();
                await using (var init = new NpgsqlCommand(await File.ReadAllTextAsync(PostgresFixture.InitSqlPath), connection))
                    await init.ExecuteNonQueryAsync();
                await PsqlScriptRunner.RunAsync(connection, await File.ReadAllTextAsync(PostgresFixture.SeedSqlPath));
            }

            var before = new Dictionary<string, int>();
            await using (var sys = Fx.CreateDbContext(cs))
            {
                foreach (var table in SchemaMigrator.InitialTables)
                    before[table] = await sys.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\"").SingleAsync();
            }
            Assert.True(before["checklists"] > 0 && before["reminders"] > 0 && before["app_settings"] == 1);

            Assert.Equal(new[] { "0002" }, await SchemaMigrator.MigrateAsync(cs, NullLogger.Instance));

            await using var factory = new IntegrationApiFactory(cs);
            var (owner, ownerId) = await RegisterConfirmAndLoginAsync(factory, TestAccounts.NewEmail("owner"));
            using (owner)
            {
                Assert.Equal(new[] { "Admin", "User" }, await RolesAsync(owner));
                var lists = await TestAccounts.JsonAsync(await owner.Client.GetAsync("/api/v1/checklists?includeArchived=true"));
                Assert.Equal(before["checklists"], lists.GetArrayLength());
                await AssertStatusAsync(HttpStatusCode.OK, await owner.Client.GetAsync("/api/v1/today"));
                Assert.Equal("Asia/Kolkata", Str(await TestAccounts.JsonAsync(await owner.Client.GetAsync("/api/v1/settings")), "timeZone"));
            }

            await using (var sys = Fx.CreateDbContext(cs))
            {
                foreach (var table in SchemaMigrator.InitialTables)
                {
                    var owned = await sys.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\" WHERE user_id = {0}", ownerId).SingleAsync();
                    var total = await sys.Database.SqlQueryRaw<int>("SELECT count(*)::int AS \"Value\" FROM \"" + table + "\"").SingleAsync();
                    Assert.True(owned == before[table] && total == before[table], $"{table}: {owned}/{total} owned, expected {before[table]}");
                }
            }
        }
        finally
        {
            await Fx.DropDatabaseAsync(cs);
            // The extra factory re-pointed this process-wide variable; restore it for later hosts.
            Environment.SetEnvironmentVariable("ConnectionStrings__Default", Fx.ConnectionString);
        }
    }
}
