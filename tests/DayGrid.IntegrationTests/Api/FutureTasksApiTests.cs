using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DayGrid.IntegrationTests.Api;

public class FutureTasksApiTests : IntegrationTestBase
{
    private const string Url = "/api/v1/future-tasks";

    public FutureTasksApiTests(PostgresFixture fixture) : base(fixture) { }

    private Task<JsonElement> CreateAsync(string title = "Pay bill", string dueDate = "2026-10-20", string? dueTime = "18:00:00", object? reminders = null) =>
        PostCreatedAsync(Url, new { title, notes = "n", dueDate, dueTime, category = "Finance", priority = "High", reminders });

    private async Task<JsonElement> FindAsync(Guid id) => (await GetOkAsync(Url)).EnumerateArray().Single(t => Id(t) == id);

    [Fact]
    public async Task Create_WithReminders_RoundTrips_AndComputesFireAtInUserZone()
    {
        var checklist = await PostCreatedAsync("/api/v1/checklists", new { name = "C" });
        var created = await PostCreatedAsync(Url, new
        {
            title = Text.Of(200), notes = Text.Emoji(300), dueDate = "2026-10-20", dueTime = "18:00:00", category = Text.Of(60),
            priority = "Critical", promoteToChecklistId = Id(checklist),
            reminders = new[]
            {
                new { offsetMinutes = 30, channels = new[] { "InApp", "email" } },
                new { offsetMinutes = 10080, channels = new[] { "bogus" } }
            }
        });

        var reminders = created.GetProperty("reminders").EnumerateArray().OrderBy(r => r.GetProperty("offsetMinutes").GetInt32()).ToList();
        Assert.Equal(2, reminders.Count);
        // 18:00 IST (+05:30) = 12:30 UTC, minus the offset.
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 12, 0, 0, TimeSpan.Zero), reminders[0].GetProperty("fireAtUtc").GetDateTimeOffset());
        Assert.Equal("InApp, Email", Str(reminders[0], "channels"));
        Assert.Equal("InApp", Str(reminders[1], "channels"));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 12, 30, 0, TimeSpan.Zero), reminders[1].GetProperty("fireAtUtc").GetDateTimeOffset());

        var listed = await FindAsync(Id(created));
        AssertJsonEquivalent(created, listed, "reminders");
        var listedReminders = listed.GetProperty("reminders").EnumerateArray().OrderBy(r => r.GetProperty("offsetMinutes").GetInt32()).ToList();
        for (var i = 0; i < 2; i++)
            AssertJsonEquivalent(reminders[i], listedReminders[i]);

        var viaSubresource = (await GetOkAsync($"{Url}/{Id(created)}/reminders")).EnumerateArray().OrderBy(r => r.GetProperty("offsetMinutes").GetInt32()).ToList();
        AssertJsonEquivalent(reminders[0], viaSubresource[0]);
    }

    [Fact]
    public async Task AllDayTask_RemindersFireAt0900Local()
    {
        var created = await CreateAsync(dueTime: null, reminders: new[] { new { offsetMinutes = 0, channels = Array.Empty<string>() } });
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 3, 30, 0, TimeSpan.Zero),
            created.GetProperty("reminders")[0].GetProperty("fireAtUtc").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(527041)]
    [InlineData(int.MaxValue)]
    public async Task ReminderOffset_OutOfRange_Returns400(int offset)
    {
        var bad = new[] { new { offsetMinutes = offset, channels = new[] { "InApp" } } };
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(Url, new { title = "x", dueDate = "2026-10-20", reminders = bad }), "offsetMinutes");

        var created = await CreateAsync();
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = "x", dueDate = "2026-10-20", reminders = bad }), "offsetMinutes");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync($"{Url}/{Id(created)}/reminders", bad[0]), "offsetMinutes");
    }

    [Fact]
    public async Task ReminderOffset_MaxAllowed_Succeeds()
    {
        var created = await CreateAsync(reminders: new[] { new { offsetMinutes = 527040, channels = new[] { "InApp" } } });
        Assert.Equal(527040, created.GetProperty("reminders")[0].GetProperty("offsetMinutes").GetInt32());
    }

    [Fact]
    public async Task Create_Validation_FkAndBoundaries()
    {
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(Url, new { title = " ", dueDate = "2026-10-20" }), "title");
        await AssertValidationErrorAsync(await Client.PostAsJsonAsync(Url, new { title = "x", dueDate = "2026-10-20", promoteToChecklistId = Guid.NewGuid() }), "promoteToChecklistId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Url, new { title = Text.Of(201), dueDate = "2026-10-20" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PostAsJsonAsync(Url, new { title = "x", dueDate = "2026-10-20", category = Text.Of(61) }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Post, Url, "{\"title\":\"x\",\"dueDate\":\"20-10-2026\"}"));
        Assert.Empty((await GetOkAsync(Url)).EnumerateArray());

        await CreateAsync(Text.Emoji(200), dueDate: "9999-12-30", dueTime: "00:00:00");
        await CreateAsync("early", dueDate: "0001-01-02", dueTime: "23:59:00");
    }

    [Fact]
    public async Task Update_MovesScheduledReminders_OrReplacesThem()
    {
        var created = await CreateAsync(reminders: new[] { new { offsetMinutes = 60, channels = new[] { "InApp" } } });

        // Reminders omitted: scheduled ones follow the new due date/time.
        var moved = await PutOkAsync($"{Url}/{Id(created)}", new { title = "Pay bill", dueDate = "2026-10-21", dueTime = "10:00:00", priority = "Low" });
        var reminder = moved.GetProperty("reminders").EnumerateArray().Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 21, 3, 30, 0, TimeSpan.Zero), reminder.GetProperty("fireAtUtc").GetDateTimeOffset());
        AssertJsonEquivalent(moved, await FindAsync(Id(created)), "reminders");

        // Reminders resent: the set is replaced.
        var replaced = await PutOkAsync($"{Url}/{Id(created)}", new
        {
            title = "Pay bill", dueDate = "2026-10-21", dueTime = "10:00:00", priority = "Low",
            reminders = new[] { new { offsetMinutes = 5, channels = new[] { "Email" } }, new { offsetMinutes = 15, channels = new[] { "Email" } } }
        });
        Assert.Equal(new[] { 5, 15 }, replaced.GetProperty("reminders").EnumerateArray().Select(r => r.GetProperty("offsetMinutes").GetInt32()).OrderBy(x => x));
        await using (var db = NewDb())
            Assert.Equal(2, await db.Reminders.CountAsync());

        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = "", dueDate = "2026-10-21" }), "title");
        await AssertValidationErrorAsync(await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = "x", dueDate = "2026-10-21", promoteToChecklistId = Guid.NewGuid() }), "promoteToChecklistId");
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.PutAsJsonAsync($"{Url}/{Id(created)}", new { title = Text.Of(201), dueDate = "2026-10-21" }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PutAsJsonAsync($"{Url}/{Guid.NewGuid()}", new { title = "x", dueDate = "2026-10-21" }));
    }

    [Fact]
    public async Task Status_DoneCancelsScheduledReminders_AndSetsCompletedAt()
    {
        var created = await CreateAsync(reminders: new[] { new { offsetMinutes = 60, channels = new[] { "InApp" } } });

        var done = await PatchOkAsync($"{Url}/{Id(created)}/status", new { status = "Done" });
        Assert.Equal("Done", Str(done, "status"));
        Assert.Equal(JsonValueKind.String, done.GetProperty("completedAt").ValueKind);
        Assert.Equal("Cancelled", Str(done.GetProperty("reminders")[0], "status"));
        AssertJsonEquivalent(done, await FindAsync(Id(created)), "reminders");

        var reopened = await PatchOkAsync($"{Url}/{Id(created)}/status", new { status = "Pending" });
        Assert.Equal(JsonValueKind.Null, reopened.GetProperty("completedAt").ValueKind);

        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsJsonAsync($"{Url}/{Guid.NewGuid()}/status", new { status = "Done" }));
        await AssertStatusAsync(HttpStatusCode.BadRequest, await SendJsonAsync(HttpMethod.Patch, $"{Url}/{Id(created)}/status", "{\"status\":\"Archived\"}"));
    }

    [Fact]
    public async Task Defer_MovesDueDateAndReminders()
    {
        var created = await CreateAsync(reminders: new[] { new { offsetMinutes = 0, channels = new[] { "InApp" } } });
        var deferred = await PatchOkAsync($"{Url}/{Id(created)}/defer", new { newDueDate = "2026-11-01" });

        Assert.Equal("Deferred", Str(deferred, "status"));
        Assert.Equal("2026-11-01", Str(deferred, "dueDate"));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 12, 30, 0, TimeSpan.Zero), deferred.GetProperty("reminders")[0].GetProperty("fireAtUtc").GetDateTimeOffset());
        AssertJsonEquivalent(deferred, await FindAsync(Id(created)), "reminders");

        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsJsonAsync($"{Url}/{Guid.NewGuid()}/defer", new { newDueDate = "2026-11-01" }));
        await AssertValidationErrorAsync(await SendJsonAsync(HttpMethod.Patch, $"{Url}/{Id(created)}/defer", "{}"), "newDueDate");
    }

    [Fact]
    public async Task Reminders_Subresource_AddListDelete()
    {
        var created = await CreateAsync();
        var remindersUrl = $"{Url}/{Id(created)}/reminders";

        var added = await PostCreatedAsync(remindersUrl, new { offsetMinutes = 1440, channels = new[] { "Email" } });
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 12, 30, 0, TimeSpan.Zero), added.GetProperty("fireAtUtc").GetDateTimeOffset());
        AssertJsonEquivalent(added, (await GetOkAsync(remindersUrl)).EnumerateArray().Single());

        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PostAsJsonAsync($"{Url}/{Guid.NewGuid()}/reminders", new { offsetMinutes = 1, channels = new[] { "InApp" } }));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Url}/{Guid.NewGuid()}/reminders/{Id(added)}"));
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{remindersUrl}/{Id(added)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{remindersUrl}/{Id(added)}"));
        Assert.Empty((await GetOkAsync(remindersUrl)).EnumerateArray());
    }

    [Fact]
    public async Task Delete_RemovesTaskAndReminders()
    {
        var created = await CreateAsync(reminders: new[] { new { offsetMinutes = 0, channels = new[] { "InApp" } } });
        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"{Url}/{Id(created)}"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.DeleteAsync($"{Url}/{Id(created)}"));
        await using var db = NewDb();
        Assert.False(await db.Reminders.AnyAsync());
    }

    [Fact]
    public async Task List_Filters_StatusRangeAndILikeSearch()
    {
        var a = await CreateAsync("Renew PASSPORT", "2026-10-10");
        var b = await CreateAsync("Dentist", "2026-11-10");
        var c = await CreateAsync("50% deposit", "2026-12-10");
        await PatchOkAsync($"{Url}/{Id(b)}/status", new { status = "Cancelled" });

        Assert.Equal(new[] { Id(a), Id(b), Id(c) }, (await GetOkAsync(Url)).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(b) }, (await GetOkAsync($"{Url}?status=Cancelled")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(b), Id(c) }, (await GetOkAsync($"{Url}?from=2026-11-01")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(a), Id(b) }, (await GetOkAsync($"{Url}?to=2026-11-10")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(a) }, (await GetOkAsync($"{Url}?q=passport")).EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(c) }, (await GetOkAsync($"{Url}?q={Uri.EscapeDataString("50%")}")).EnumerateArray().Select(Id));
        Assert.Empty((await GetOkAsync($"{Url}?q=nothing")).EnumerateArray());
    }

    [Fact]
    public async Task Upcoming_GroupsByHorizon_AndCapsDays()
    {
        var today = await CreateAsync("today", Today.ToString("yyyy-MM-dd"));
        var tomorrow = await CreateAsync("tomorrow", Today.AddDays(1).ToString("yyyy-MM-dd"));
        var week = await CreateAsync("week", Today.AddDays(7).ToString("yyyy-MM-dd"));
        var later = await CreateAsync("later", Today.AddDays(30).ToString("yyyy-MM-dd"));
        var beyond = await CreateAsync("beyond cap", Today.AddDays(3661).ToString("yyyy-MM-dd"));
        await CreateAsync("past", Today.AddDays(-1).ToString("yyyy-MM-dd"));

        var grouped = await GetOkAsync($"{Url}/upcoming?days=30");
        Assert.Equal(new[] { Id(today) }, grouped.GetProperty("today").EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(tomorrow) }, grouped.GetProperty("tomorrow").EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(week) }, grouped.GetProperty("thisWeek").EnumerateArray().Select(Id));
        Assert.Equal(new[] { Id(later) }, grouped.GetProperty("later").EnumerateArray().Select(Id));

        var capped = await GetOkAsync($"{Url}/upcoming?days=999999");
        Assert.DoesNotContain(Id(beyond), capped.GetProperty("later").EnumerateArray().Select(Id));
        Assert.Single((await GetOkAsync($"{Url}/upcoming?days=0")).GetProperty("later").EnumerateArray()); // 0 => 30-day default
        await AssertStatusAsync(HttpStatusCode.BadRequest, await Client.GetAsync($"{Url}/upcoming"));
    }

    [Fact]
    public async Task Notifications_List_MarkRead_ReadAll_Test()
    {
        await using (var db = NewDb())
        {
            db.NotificationLogs.AddRange(
                new NotificationLog { Title = Text.Of(200), Body = Text.Emoji(1000), CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2) },
                new NotificationLog { Title = "newer", Body = "b", Channel = NotificationChannel.Email, CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1) });
            await db.SaveChangesAsync();
        }

        var all = (await GetOkAsync("/api/v1/notifications")).EnumerateArray().ToList();
        Assert.Equal(new[] { "newer", Text.Of(200) }, all.Select(n => Str(n, "title")));
        Assert.Equal(Text.Emoji(1000), Str(all[1], "body"));
        Assert.Single((await GetOkAsync("/api/v1/notifications?take=1")).EnumerateArray());
        Assert.Single((await GetOkAsync("/api/v1/notifications?take=0")).EnumerateArray()); // clamped to 1

        var read = await PatchOkAsync($"/api/v1/notifications/{Id(all[0])}/read");
        Assert.Equal(JsonValueKind.String, read.GetProperty("readAtUtc").ValueKind);
        Assert.Single((await GetOkAsync("/api/v1/notifications?unreadOnly=true")).EnumerateArray());
        await AssertStatusAsync(HttpStatusCode.NotFound, await Client.PatchAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null));

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.PatchAsync("/api/v1/notifications/read-all", null));
        Assert.Empty((await GetOkAsync("/api/v1/notifications?unreadOnly=true")).EnumerateArray());

        // init.sql seeds email_enabled=true + an address; the sender is faked.
        var test = await ReadJsonAsync(await Client.PostAsync("/api/v1/notifications/test", null));
        Assert.True(test.GetProperty("inAppSent").GetBoolean());
        Assert.True(test.GetProperty("emailAttempted").GetBoolean());
        Assert.Equal(JsonValueKind.Null, test.GetProperty("emailError").ValueKind);
        Assert.Single(Fx.Factory.Email.Sent);
        Assert.Equal(3, (await GetOkAsync("/api/v1/notifications")).GetArrayLength());

        Fx.Factory.Email.FailWith = new InvalidOperationException("smtp down");
        var failed = await ReadJsonAsync(await Client.PostAsync("/api/v1/notifications/test", null));
        Assert.Equal("smtp down", Str(failed, "emailError"));
    }
}
