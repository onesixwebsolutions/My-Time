using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.IntegrationTests.Infrastructure;
using DayGrid.TestSupport;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DayGrid.IntegrationTests.Auth;

/// <summary>
/// User A (the default test user) owns one of everything; user B must not be able to read,
/// change, delete, list, link to or reorder any of it — and every id-addressed route is covered
/// (a meta-test fails when a new route is added without an isolation case).
/// </summary>
public class TenantIsolationTests : IntegrationTestBase
{
    private const string Secret = "ALICE-SECRET";
    private const string Day = "2026-10-05";    // the frozen "today" (Monday)
    private const string OverrideDay = "2026-10-06";

    public TenantIsolationTests(PostgresFixture fixture) : base(fixture) { }

    private sealed record AliceData(
        Guid Checklist, Guid Item, Guid Template, Guid Block, Guid Assignment, Guid FutureTask, Guid Reminder,
        Guid Notification, Guid SimpleTask, Guid Constant, Guid Varying, Guid Spend)
    {
        public IEnumerable<Guid> All => [Checklist, Item, Template, Block, Assignment, FutureTask, Reminder, Notification, SimpleTask, Constant, Varying, Spend];
    }

    private sealed record Case(string Pattern, HttpMethod Method, string Url, object? Body = null);

    private async Task<AliceData> SeedAliceAsync()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = $"{Secret} list", sortOrder = 5 });
        var template = await PostCreatedAsync("/api/v1/timetable/templates", new { name = $"{Secret} template", dayStart = "06:00:00", dayEnd = "22:00:00", slotMinutes = 30 });
        await PatchOkAsync($"/api/v1/timetable/templates/{Id(template)}/default");
        var block = await PostCreatedAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new
        {
            title = $"{Secret} block", startTime = "09:00:00", endTime = "10:00:00", category = "Work",
            checklistId = Id(checklist), allowOverlap = false, notifyAtStart = false, sortOrder = 0
        });
        var item = await PostCreatedAsync($"/api/v1/checklists/{Id(checklist)}/items", new
        {
            title = $"{Secret} item", anchorType = "LinkedToBlock", timetableBlockId = Id(block), recurrence = new { type = "Daily" }
        });
        await AssertStatusAsync(HttpStatusCode.OK, await Client.PostAsJsonAsync($"/api/v1/items/{Id(item)}/complete", new { date = Day }));
        var assignment = await PostCreatedAsync("/api/v1/timetable/assignments", new { templateId = Id(template), scope = "Weekday", dayOfWeek = "Monday", priority = 1 });
        await PutOkAsync($"/api/v1/day-overrides/{OverrideDay}", new { mode = "UseTemplate", templateId = Id(template), note = Secret });
        var futureTask = await PostCreatedAsync("/api/v1/future-tasks", new
        {
            title = $"{Secret} task", dueDate = Day, promoteToChecklistId = Id(checklist),
            reminders = new[] { new { offsetMinutes = 30, channels = new[] { "InApp" } } }
        });
        var reminder = futureTask.GetProperty("reminders")[0].GetProperty("id").GetGuid();
        await AssertStatusAsync(HttpStatusCode.OK, await Client.PostAsync("/api/v1/notifications/test", null));
        var notification = Id((await GetOkAsync("/api/v1/notifications")).EnumerateArray().Single());
        var simple = await PostCreatedAsync("/api/v1/tasks", new { title = $"{Secret} simple" });
        var constant = await PostCreatedAsync("/api/v1/expenses/constant", new { name = $"{Secret} rent", amount = 100m });
        var varying = await PostCreatedAsync("/api/v1/expenses/varying", new { title = $"{Secret} varying", amount = 5m, date = Day });
        var spend = await PostCreatedAsync("/api/v1/expenses/spends", new { title = $"{Secret} spend", amount = 7m, date = Day });
        await PutOkAsync("/api/v1/settings", new
        {
            timeZone = "Asia/Kolkata", weekStartsOn = "Sunday", dayStart = "05:00:00", dayEnd = "21:00:00", defaultSlotMinutes = 15,
            emailEnabled = true, emailTo = "alice@example.test", dailyDigestTime = "06:15:00", theme = Secret
        });

        return new AliceData(Id(checklist), Id(item), Id(template), Id(block), Id(assignment), Id(futureTask), reminder,
            notification, Id(simple), Id(constant), Id(varying), Id(spend));
    }

    private static List<Case> IdAddressedCases(AliceData a)
    {
        var task = new { title = "x", notes = (string?)null, priority = "Normal" };
        var range = $"from={Day}&to={Day}";
        return
        [
            new("/api/v1/checklists/{id:guid}", HttpMethod.Get, $"/api/v1/checklists/{a.Checklist}"),
            new("/api/v1/checklists/{id:guid}", HttpMethod.Put, $"/api/v1/checklists/{a.Checklist}", new { name = "hijack", sortOrder = 0 }),
            new("/api/v1/checklists/{id:guid}/archive", HttpMethod.Patch, $"/api/v1/checklists/{a.Checklist}/archive?archived=true"),
            new("/api/v1/checklists/{id:guid}", HttpMethod.Delete, $"/api/v1/checklists/{a.Checklist}"),
            new("/api/v1/checklists/{id:guid}/items", HttpMethod.Get, $"/api/v1/checklists/{a.Checklist}/items"),
            new("/api/v1/checklists/{id:guid}/items", HttpMethod.Post, $"/api/v1/checklists/{a.Checklist}/items", new { title = "planted" }),
            new("/api/v1/checklists/{id:guid}/items/reorder", HttpMethod.Put, $"/api/v1/checklists/{a.Checklist}/items/reorder", new { items = new[] { new { id = a.Item, sortOrder = 99 } } }),
            new("/api/v1/items/{itemId:guid}", HttpMethod.Put, $"/api/v1/items/{a.Item}", new { title = "hijack", isActive = true }),
            new("/api/v1/items/{itemId:guid}", HttpMethod.Delete, $"/api/v1/items/{a.Item}"),
            new("/api/v1/items/{itemId:guid}/active", HttpMethod.Patch, $"/api/v1/items/{a.Item}/active?active=false"),
            new("/api/v1/items/{itemId:guid}/history", HttpMethod.Get, $"/api/v1/items/{a.Item}/history?{range}"),
            new("/api/v1/items/{itemId:guid}/complete", HttpMethod.Post, $"/api/v1/items/{a.Item}/complete", new { date = Day, status = "Skipped" }),
            new("/api/v1/items/{itemId:guid}/complete", HttpMethod.Delete, $"/api/v1/items/{a.Item}/complete?date={Day}"),
            new("/api/v1/items/{itemId:guid}/skip", HttpMethod.Post, $"/api/v1/items/{a.Item}/skip", new { date = Day }),
            new("/api/v1/timetable/templates/{id:guid}", HttpMethod.Get, $"/api/v1/timetable/templates/{a.Template}"),
            new("/api/v1/timetable/templates/{id:guid}", HttpMethod.Put, $"/api/v1/timetable/templates/{a.Template}", new { name = "hijack", dayStart = "06:00:00", dayEnd = "22:00:00", slotMinutes = 30 }),
            new("/api/v1/timetable/templates/{id:guid}", HttpMethod.Delete, $"/api/v1/timetable/templates/{a.Template}"),
            new("/api/v1/timetable/templates/{id:guid}/duplicate", HttpMethod.Post, $"/api/v1/timetable/templates/{a.Template}/duplicate"),
            new("/api/v1/timetable/templates/{id:guid}/default", HttpMethod.Patch, $"/api/v1/timetable/templates/{a.Template}/default"),
            new("/api/v1/timetable/templates/{id:guid}/blocks", HttpMethod.Get, $"/api/v1/timetable/templates/{a.Template}/blocks"),
            new("/api/v1/timetable/templates/{id:guid}/blocks", HttpMethod.Post, $"/api/v1/timetable/templates/{a.Template}/blocks",
                new { title = "planted", startTime = "11:00:00", endTime = "12:00:00", category = "Work", allowOverlap = true, notifyAtStart = false, sortOrder = 0 }),
            new("/api/v1/timetable/blocks/{blockId:guid}", HttpMethod.Put, $"/api/v1/timetable/blocks/{a.Block}",
                new { title = "hijack", startTime = "09:00:00", endTime = "10:00:00", category = "Work", allowOverlap = true, notifyAtStart = false, sortOrder = 0 }),
            new("/api/v1/timetable/blocks/{blockId:guid}", HttpMethod.Delete, $"/api/v1/timetable/blocks/{a.Block}"),
            new("/api/v1/timetable/blocks/{blockId:guid}/move", HttpMethod.Put, $"/api/v1/timetable/blocks/{a.Block}/move", new { startTime = "13:00:00", endTime = "14:00:00" }),
            new("/api/v1/timetable/assignments/{id:guid}", HttpMethod.Delete, $"/api/v1/timetable/assignments/{a.Assignment}"),
            new("/api/v1/day-overrides/{date}", HttpMethod.Get, $"/api/v1/day-overrides/{OverrideDay}"),
            new("/api/v1/future-tasks/{id:guid}", HttpMethod.Put, $"/api/v1/future-tasks/{a.FutureTask}", new { title = "hijack", dueDate = Day, priority = "Normal" }),
            new("/api/v1/future-tasks/{id:guid}", HttpMethod.Delete, $"/api/v1/future-tasks/{a.FutureTask}"),
            new("/api/v1/future-tasks/{id:guid}/status", HttpMethod.Patch, $"/api/v1/future-tasks/{a.FutureTask}/status", new { status = "Done" }),
            new("/api/v1/future-tasks/{id:guid}/defer", HttpMethod.Patch, $"/api/v1/future-tasks/{a.FutureTask}/defer", new { newDueDate = "2027-01-01" }),
            new("/api/v1/future-tasks/{id:guid}/reminders/", HttpMethod.Get, $"/api/v1/future-tasks/{a.FutureTask}/reminders"),
            new("/api/v1/future-tasks/{id:guid}/reminders/", HttpMethod.Post, $"/api/v1/future-tasks/{a.FutureTask}/reminders", new { offsetMinutes = 1, channels = new[] { "InApp" } }),
            new("/api/v1/future-tasks/{id:guid}/reminders/{rid:guid}", HttpMethod.Delete, $"/api/v1/future-tasks/{a.FutureTask}/reminders/{a.Reminder}"),
            new("/api/v1/notifications/{id:guid}/read", HttpMethod.Patch, $"/api/v1/notifications/{a.Notification}/read"),
            new("/api/v1/tasks/{id:guid}", HttpMethod.Put, $"/api/v1/tasks/{a.SimpleTask}", task),
            new("/api/v1/tasks/{id:guid}/status", HttpMethod.Patch, $"/api/v1/tasks/{a.SimpleTask}/status", new { status = "Done" }),
            new("/api/v1/tasks/{id:guid}", HttpMethod.Delete, $"/api/v1/tasks/{a.SimpleTask}"),
            new("/api/v1/expenses/constant/{id:guid}", HttpMethod.Get, $"/api/v1/expenses/constant/{a.Constant}"),
            new("/api/v1/expenses/constant/{id:guid}", HttpMethod.Put, $"/api/v1/expenses/constant/{a.Constant}", new { name = "hijack", amount = 1m }),
            new("/api/v1/expenses/constant/{id:guid}/active", HttpMethod.Patch, $"/api/v1/expenses/constant/{a.Constant}/active?active=false"),
            new("/api/v1/expenses/constant/{id:guid}", HttpMethod.Delete, $"/api/v1/expenses/constant/{a.Constant}"),
            new("/api/v1/expenses/varying/{id:guid}", HttpMethod.Get, $"/api/v1/expenses/varying/{a.Varying}"),
            new("/api/v1/expenses/varying/{id:guid}", HttpMethod.Put, $"/api/v1/expenses/varying/{a.Varying}", new { title = "hijack", amount = 1m, date = Day }),
            new("/api/v1/expenses/varying/{id:guid}", HttpMethod.Delete, $"/api/v1/expenses/varying/{a.Varying}"),
            new("/api/v1/expenses/spends/{id:guid}", HttpMethod.Get, $"/api/v1/expenses/spends/{a.Spend}"),
            new("/api/v1/expenses/spends/{id:guid}", HttpMethod.Put, $"/api/v1/expenses/spends/{a.Spend}", new { title = "hijack", amount = 1m, date = Day }),
            new("/api/v1/expenses/spends/{id:guid}", HttpMethod.Delete, $"/api/v1/expenses/spends/{a.Spend}"),
        ];
    }

    /// <summary>Routes with a parameter that are keyed by something other than another user's row id.</summary>
    private static readonly Dictionary<string, string> NotIdAddressed = new()
    {
        ["/api/v1/days/{date}/summary"] = "the caller's own day (covered by OwnDayViews_ShowOnlyOwnData)",
        ["/api/v1/day-overrides/{date}|PUT"] = "upserts the caller's own override (covered by DayOverrides_ArePerUser)",
        ["/api/v1/day-overrides/{date}|DELETE"] = "deletes the caller's own override (covered by DayOverrides_ArePerUser)",
        ["/api/v1/admin/users/{id:guid}/lock"] = "admin account management (AdminTests)",
        ["/api/v1/admin/users/{id:guid}/unlock"] = "admin account management (AdminTests)",
        ["/api/v1/admin/users/{id:guid}/resend-confirmation"] = "admin account management (AdminTests)",
        ["/api/v1/admin/users/{id:guid}"] = "admin account management (AdminTests)",
        ["/api/{**path}"] = "unknown-route fallback",
    };

    [Fact]
    public async Task EveryIdAddressedRoute_Returns404ForAnotherUsersRow_AndChangesNothing()
    {
        var alice = await SeedAliceAsync();
        var before = await AliceSnapshotAsync();

        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            foreach (var c in IdAddressedCases(alice))
            {
                var request = new HttpRequestMessage(c.Method, c.Url);
                if (c.Body is not null)
                    request.Content = JsonContent.Create(c.Body);
                var response = await bob.Client.SendAsync(request);
                Assert.True(response.StatusCode == HttpStatusCode.NotFound,
                    $"{c.Method} {c.Url} as another user: expected 404, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }
        }

        Assert.Equal(before, await AliceSnapshotAsync());
    }

    [Fact]
    public void IsolationCases_CoverEveryParameterisedApiRoute()
    {
        var dummy = new AliceData(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        var covered = IdAddressedCases(dummy).Select(c => $"{c.Pattern}|{c.Method.Method}").ToHashSet();

        var missing = new List<string>();
        foreach (var endpoint in Fx.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>())
        {
            var pattern = endpoint.RoutePattern.RawText ?? string.Empty;
            if (!pattern.StartsWith("/api/", StringComparison.Ordinal) || !pattern.Contains('{'))
                continue;
            foreach (var method in endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"])
            {
                var key = $"{pattern}|{method}";
                if (!covered.Contains(key) && !NotIdAddressed.ContainsKey(pattern) && !NotIdAddressed.ContainsKey(key))
                    missing.Add(key);
            }
        }

        Assert.True(missing.Count == 0, "Routes without a tenant-isolation case:\n" + string.Join("\n", missing));
    }

    [Fact]
    public async Task ListsSearchesAndViews_NeverContainAnotherUsersData()
    {
        var alice = await SeedAliceAsync();
        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            var range = $"from=2026-09-01&to=2026-12-31";
            var urls = new[]
            {
                "/api/v1/checklists?includeArchived=true", "/api/v1/timetable/templates", "/api/v1/timetable/assignments",
                "/api/v1/day-overrides", "/api/v1/future-tasks", $"/api/v1/future-tasks?q={Secret}", "/api/v1/future-tasks/upcoming?days=365",
                "/api/v1/notifications", "/api/v1/tasks", $"/api/v1/tasks?q={Secret}", "/api/v1/expenses/constant?includeInactive=true",
                $"/api/v1/expenses/varying?{range}", $"/api/v1/expenses/spends?{range}", "/api/v1/today", $"/api/v1/today?date={OverrideDay}",
                "/api/v1/today/now", $"/api/v1/days/{Day}/summary", $"/api/v1/days/range?from={Day}&to=2026-10-12",
                "/api/v1/stats/streaks", $"/api/v1/stats/completion?{range}", "/api/v1/export",
                $"/api/v1/timetable/resolve?date={Day}", $"/api/v1/timetable/resolve?date={OverrideDay}", "/api/v1/settings"
            };
            foreach (var url in urls)
            {
                var response = await bob.Client.GetAsync(url);
                await AssertStatusAsync(HttpStatusCode.OK, response);
                var body = await response.Content.ReadAsStringAsync();
                Assert.DoesNotContain(Secret, body);
                foreach (var id in alice.All)
                    Assert.DoesNotContain(id.ToString(), body, StringComparison.OrdinalIgnoreCase);
            }

            // Bob's resolve finds no template at all (Alice's default/assignment/override are hers).
            var resolved = await TestAccounts.JsonAsync(await bob.Client.GetAsync($"/api/v1/timetable/resolve?date={Day}"));
            Assert.Equal(JsonValueKind.Null, resolved.GetProperty("templateId").ValueKind);
            var validate = await bob.Client.PostAsJsonAsync("/api/v1/timetable/blocks/validate", new { templateId = alice.Template, startTime = "09:00:00", endTime = "10:00:00", allowOverlap = false });
            var validation = await TestAccounts.JsonAsync(validate);
            Assert.True(validation.GetProperty("valid").GetBoolean());
            Assert.Empty(validation.GetProperty("conflicts").EnumerateArray());
        }
    }

    [Fact]
    public async Task OwnDayViews_ShowOnlyOwnData()
    {
        await SeedAliceAsync();
        var aliceToday = await GetOkAsync("/api/v1/today");
        Assert.Contains(Secret, aliceToday.GetRawText());
        Assert.NotEmpty(aliceToday.GetProperty("blocks").EnumerateArray());

        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            var bobToday = await TestAccounts.JsonAsync(await bob.Client.GetAsync("/api/v1/today"));
            Assert.Empty(bobToday.GetProperty("blocks").EnumerateArray());
            Assert.Empty(bobToday.GetProperty("checklists").EnumerateArray());
            Assert.Empty(bobToday.GetProperty("dueToday").EnumerateArray());
            var days = await TestAccounts.JsonAsync(await bob.Client.GetAsync($"/api/v1/days/range?from={Day}&to=2026-10-07"));
            Assert.All(days.EnumerateArray(), d => Assert.DoesNotContain(Secret, d.GetRawText()));
        }
    }

    [Fact]
    public async Task LinkingToAnotherUsersRows_IsRejected()
    {
        var alice = await SeedAliceAsync();
        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            async Task<JsonElement> Create(string url, object body)
            {
                var response = await bob.Client.PostAsJsonAsync(url, body);
                await AssertStatusAsync(HttpStatusCode.Created, response);
                return await TestAccounts.JsonAsync(response);
            }

            var checklist = await Create("/api/v1/checklists", new { name = "bob" });
            var template = await Create("/api/v1/timetable/templates", new { name = "bob", dayStart = "06:00:00", dayEnd = "22:00:00", slotMinutes = 30 });
            var block = await Create($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "b", startTime = "08:00:00", endTime = "09:00:00", category = "Work", allowOverlap = false, notifyAtStart = false, sortOrder = 0 });
            var item = await Create($"/api/v1/checklists/{Id(checklist)}/items", new { title = "i" });
            var task = await Create("/api/v1/future-tasks", new { title = "t", dueDate = Day });

            var attempts = new (string What, Func<Task<HttpResponseMessage>> Call)[]
            {
                ("item -> Alice's block", () => bob.Client.PostAsJsonAsync($"/api/v1/checklists/{Id(checklist)}/items", new { title = "x", timetableBlockId = alice.Block })),
                ("item update -> Alice's block", () => bob.Client.PutAsJsonAsync($"/api/v1/items/{Id(item)}", new { title = "x", isActive = true, timetableBlockId = alice.Block })),
                ("block -> Alice's checklist", () => bob.Client.PostAsJsonAsync($"/api/v1/timetable/templates/{Id(template)}/blocks", new { title = "x", startTime = "10:00:00", endTime = "11:00:00", category = "Work", checklistId = alice.Checklist, allowOverlap = true, notifyAtStart = false, sortOrder = 0 })),
                ("block update -> Alice's checklist", () => bob.Client.PutAsJsonAsync($"/api/v1/timetable/blocks/{Id(block)}", new { title = "x", startTime = "08:00:00", endTime = "09:00:00", category = "Work", checklistId = alice.Checklist, allowOverlap = true, notifyAtStart = false, sortOrder = 0 })),
                ("assignment -> Alice's template", () => bob.Client.PostAsJsonAsync("/api/v1/timetable/assignments", new { templateId = alice.Template, scope = "Weekday", dayOfWeek = "Monday", priority = 0 })),
                ("override -> Alice's template", () => bob.Client.PutAsJsonAsync($"/api/v1/day-overrides/{Day}", new { mode = "UseTemplate", templateId = alice.Template })),
                ("future task -> Alice's checklist", () => bob.Client.PostAsJsonAsync("/api/v1/future-tasks", new { title = "x", dueDate = Day, promoteToChecklistId = alice.Checklist })),
                ("future task update -> Alice's checklist", () => bob.Client.PutAsJsonAsync($"/api/v1/future-tasks/{Id(task)}", new { title = "x", dueDate = Day, promoteToChecklistId = alice.Checklist })),
            };
            foreach (var (what, call) in attempts)
            {
                var response = await call();
                Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                    $"{what}: expected 400, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            }

            // Reorder endpoints silently ignore ids the caller does not own.
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.PutAsJsonAsync("/api/v1/checklists/reorder", new { items = new[] { new { id = alice.Checklist, sortOrder = 42 }, new { id = Id(checklist), sortOrder = 1 } } }));
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.PutAsJsonAsync("/api/v1/tasks/reorder", new { items = new[] { new { id = alice.SimpleTask, sortOrder = 42 } } }));
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.PutAsJsonAsync($"/api/v1/checklists/{Id(checklist)}/items/reorder", new { items = new[] { new { id = alice.Item, sortOrder = 42 } } }));
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.PatchAsync("/api/v1/notifications/read-all", null));
        }

        var aliceChecklist = await GetOkAsync($"/api/v1/checklists/{alice.Checklist}");
        Assert.Equal(5, aliceChecklist.GetProperty("sortOrder").GetInt32());
        Assert.Equal(0, aliceChecklist.GetProperty("items")[0].GetProperty("sortOrder").GetInt32());
        Assert.Equal(0, (await GetOkAsync("/api/v1/tasks")).EnumerateArray().Single().GetProperty("sortOrder").GetInt32());
        Assert.Equal(JsonValueKind.Null, (await GetOkAsync("/api/v1/notifications")).EnumerateArray().Single().GetProperty("readAtUtc").ValueKind);
        Assert.Single((await GetOkAsync("/api/v1/timetable/assignments")).EnumerateArray());
    }

    [Fact]
    public async Task DayOverrides_ArePerUser()
    {
        await SeedAliceAsync();
        var (bob, _, _) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            await AssertStatusAsync(HttpStatusCode.NotFound, await bob.Client.GetAsync($"/api/v1/day-overrides/{OverrideDay}"));
            await AssertStatusAsync(HttpStatusCode.OK, await bob.Client.PutAsJsonAsync($"/api/v1/day-overrides/{OverrideDay}", new { mode = "RestDay", note = "bob" }));
            Assert.Equal("RestDay", Str(await TestAccounts.JsonAsync(await bob.Client.GetAsync($"/api/v1/day-overrides/{OverrideDay}")), "mode"));
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.DeleteAsync($"/api/v1/day-overrides/{OverrideDay}"));
            await AssertStatusAsync(HttpStatusCode.NoContent, await bob.Client.DeleteAsync($"/api/v1/day-overrides/{OverrideDay}"));
        }

        var mine = await GetOkAsync($"/api/v1/day-overrides/{OverrideDay}");
        Assert.Equal("UseTemplate", Str(mine, "mode"));
        Assert.Equal(Secret, Str(mine, "note"));
    }

    [Fact]
    public async Task Settings_ArePerUser()
    {
        await SeedAliceAsync();
        var (bob, _, bobEmail) = await SignInAnotherUserAsync("bob");
        using (bob)
        {
            var bobs = await TestAccounts.JsonAsync(await bob.Client.GetAsync("/api/v1/settings"));
            Assert.Equal("system", Str(bobs, "theme"));
            Assert.Equal(bobEmail, Str(bobs, "emailTo"));
            await AssertStatusAsync(HttpStatusCode.OK, await bob.Client.PutAsJsonAsync("/api/v1/settings", new
            {
                timeZone = "Europe/Paris", weekStartsOn = "Monday", dayStart = "07:00:00", dayEnd = "23:00:00", defaultSlotMinutes = 30,
                emailEnabled = false, emailTo = (string?)null, dailyDigestTime = (string?)null, theme = "light"
            }));
        }

        var mine = await GetOkAsync("/api/v1/settings");
        Assert.Equal(Secret, Str(mine, "theme"));
        Assert.Equal("Asia/Kolkata", Str(mine, "timeZone"));
        Assert.Equal("Sunday", Str(mine, "weekStartsOn"));
    }

    /// <summary>Everything Alice can see, serialized — any change by Bob would show up here.</summary>
    private async Task<string> AliceSnapshotAsync()
    {
        var parts = new List<string>();
        foreach (var url in new[]
                 {
                     "/api/v1/checklists?includeArchived=true", "/api/v1/timetable/templates", "/api/v1/timetable/assignments",
                     "/api/v1/day-overrides", "/api/v1/future-tasks", "/api/v1/notifications", "/api/v1/tasks",
                     "/api/v1/expenses/constant?includeInactive=true", "/api/v1/expenses/varying", "/api/v1/expenses/spends",
                     "/api/v1/settings", $"/api/v1/today?date={Day}"
                 })
            parts.Add(url + " => " + await (await Client.GetAsync(url)).Content.ReadAsStringAsync());

        var checklistId = Id((await GetOkAsync("/api/v1/checklists")).EnumerateArray().Single());
        parts.Add(await (await Client.GetAsync($"/api/v1/checklists/{checklistId}")).Content.ReadAsStringAsync());
        var templateId = Id((await GetOkAsync("/api/v1/timetable/templates")).EnumerateArray().Single());
        parts.Add(await (await Client.GetAsync($"/api/v1/timetable/templates/{templateId}")).Content.ReadAsStringAsync());
        return string.Join("\n", parts);
    }
}
