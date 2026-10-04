using System.Net;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Infrastructure.Data;
using DayGrid.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests.Schema;

public class SchemaBootstrapperTests : IntegrationTestBase
{
    public SchemaBootstrapperTests(PostgresFixture fixture) : base(fixture) { }

    private static async Task<long> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    private const string CountTables =
        "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'";

    [Fact]
    public async Task EnsureSchema_AppliesOnEmptyDatabase_ThenIsANoOp()
    {
        var cs = await Fx.CreateEmptyDatabaseAsync("boot");
        try
        {
            Assert.Equal(0, await ScalarAsync(cs, CountTables));

            Assert.True(await SchemaBootstrapper.EnsureSchemaAsync(cs, PostgresFixture.InitSqlPath, NullLogger.Instance));
            Assert.Equal(15, await ScalarAsync(cs, CountTables));
            Assert.Equal(1, await ScalarAsync(cs, "SELECT count(*) FROM app_settings"));

            await ScalarAsync(cs, "INSERT INTO simple_tasks (title) VALUES ('survives a second bootstrap') RETURNING 1");

            Assert.False(await SchemaBootstrapper.EnsureSchemaAsync(cs, PostgresFixture.InitSqlPath, NullLogger.Instance));
            Assert.Equal(15, await ScalarAsync(cs, CountTables));
            Assert.Equal(1, await ScalarAsync(cs, "SELECT count(*) FROM simple_tasks"));
            Assert.Equal(1, await ScalarAsync(cs, "SELECT count(*) FROM app_settings"));
        }
        finally
        {
            await Fx.DropDatabaseAsync(cs);
        }
    }

    [Fact]
    public async Task EnsureSchema_FailingScript_RollsBackCompletely()
    {
        var cs = await Fx.CreateEmptyDatabaseAsync("bootfail");
        var brokenScript = Path.Combine(Path.GetTempPath(), $"daygrid-broken-{Guid.NewGuid():N}.sql");
        try
        {
            await File.WriteAllTextAsync(brokenScript,
                await File.ReadAllTextAsync(PostgresFixture.InitSqlPath) + "\nSELECT this_is_not_valid_sql(;\n");

            await Assert.ThrowsAsync<PostgresException>(() =>
                SchemaBootstrapper.EnsureSchemaAsync(cs, brokenScript, NullLogger.Instance));

            Assert.Equal(0, await ScalarAsync(cs, CountTables));
        }
        finally
        {
            File.Delete(brokenScript);
            await Fx.DropDatabaseAsync(cs);
        }
    }

    [Fact]
    public async Task EnsureSchema_MissingScriptOnEmptyDatabase_ThrowsFileNotFound()
    {
        var cs = await Fx.CreateEmptyDatabaseAsync("bootmissing");
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() =>
                SchemaBootstrapper.EnsureSchemaAsync(cs, Path.Combine(Path.GetTempPath(), "no-such-init.sql"), NullLogger.Instance));
        }
        finally
        {
            await Fx.DropDatabaseAsync(cs);
        }
    }

    [Fact]
    public void InitSql_HasNoCreateExtension_ForAzureFlexibleServer()
    {
        var sql = File.ReadAllText(PostgresFixture.InitSqlPath);
        var statements = string.Join("\n", sql.Split('\n').Where(l => !l.TrimStart().StartsWith("--")));
        Assert.DoesNotContain("CREATE EXTENSION", statements, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SeedSql_AppliesCleanly_AfterInitSql()
    {
        var cs = await Fx.CreateEmptyDatabaseAsync("seed");
        try
        {
            Assert.True(await SchemaBootstrapper.EnsureSchemaAsync(cs, PostgresFixture.InitSqlPath, NullLogger.Instance));

            await using (var connection = new NpgsqlConnection(cs))
            {
                await connection.OpenAsync();
                var statements = await PsqlScriptRunner.RunAsync(connection, await File.ReadAllTextAsync(PostgresFixture.SeedSqlPath));
                Assert.True(statements > 20);
            }

            Assert.True(await ScalarAsync(cs, "SELECT count(*) FROM checklists") > 0);
            Assert.True(await ScalarAsync(cs, "SELECT count(*) FROM checklist_items") > 0);
            Assert.True(await ScalarAsync(cs, "SELECT count(*) FROM timetable_blocks") > 0);
            Assert.True(await ScalarAsync(cs, "SELECT count(*) FROM reminders") > 0);
            Assert.True(await ScalarAsync(cs, "SELECT count(*) FROM simple_tasks") > 0);

            // ...and every seeded row is readable through the EF model (enums, jsonb recurrence, times).
            await using var db = Fx.CreateDbContext(cs);
            Assert.NotEmpty(await db.Checklists.Include(c => c.Items).ThenInclude(i => i.Completions).ToListAsync());
            Assert.NotEmpty(await db.TimetableTemplates.Include(t => t.Blocks).Include(t => t.Assignments).ToListAsync());
            Assert.NotEmpty(await db.FutureTasks.Include(t => t.Reminders).ToListAsync());
            Assert.NotEmpty(await db.SimpleTasks.ToListAsync());
            await db.DayOverrides.ToListAsync();
            await db.NotificationLogs.ToListAsync();
            Assert.NotNull(await db.AppSettings.SingleAsync());
        }
        finally
        {
            await Fx.DropDatabaseAsync(cs);
        }
    }
}

public class ConstraintAndCascadeTests : IntegrationTestBase
{
    public ConstraintAndCascadeTests(PostgresFixture fixture) : base(fixture) { }

    private async Task<(Checklist Checklist, ChecklistItem Item, FutureTask Task)> SeedOwnersAsync()
    {
        await using var db = NewDb();
        var checklist = new Checklist { Name = "Owner" };
        var item = new ChecklistItem { ChecklistId = checklist.Id, Title = "Item" };
        var task = new FutureTask { Title = "Task", DueDate = new DateOnly(2026, 12, 1) };
        db.AddRange(checklist, item, task);
        await db.SaveChangesAsync();
        return (checklist, item, task);
    }

    private static PostgresException InnerPostgres(Exception ex) =>
        Assert.IsType<PostgresException>(ex is DbUpdateException ? ex.InnerException : ex);

    [Fact]
    public async Task ReminderCheckConstraint_RequiresExactlyOneOwner()
    {
        var (_, item, task) = await SeedOwnersAsync();

        // Neither owner.
        await using (var db = NewDb())
        {
            db.Reminders.Add(new Reminder { OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow });
            var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
            Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
            Assert.Equal("ck_reminders_exactly_one_owner", ex.ConstraintName);
        }

        // Both owners.
        await using (var db = NewDb())
        {
            db.Reminders.Add(new Reminder { FutureTaskId = task.Id, ChecklistItemId = item.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow });
            var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
            Assert.Equal("ck_reminders_exactly_one_owner", ex.ConstraintName);
        }

        // Exactly one owner, either kind.
        await using (var db = NewDb())
        {
            db.Reminders.Add(new Reminder { FutureTaskId = task.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow });
            db.Reminders.Add(new Reminder { ChecklistItemId = item.Id, OffsetMinutes = 5, FireAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            Assert.Equal(2, await db.Reminders.CountAsync());
        }
    }

    [Fact]
    public async Task TimetableBlockCheckConstraint_RejectsEndNotAfterStart()
    {
        await using var db = NewDb();
        var template = new TimetableTemplate { Name = "T" };
        db.Add(template);
        await db.SaveChangesAsync();

        db.Add(new TimetableBlock { TemplateId = template.Id, Title = "Zero length", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(9, 0) });
        var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
        Assert.Equal("ck_timetable_blocks_end_after_start", ex.ConstraintName);
    }

    [Fact]
    public async Task AppSettings_IsASingleton()
    {
        await using var db = NewDb();
        db.AppSettings.Add(new AppSetting { Id = 2 });
        var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
        Assert.True(ex.SqlState is PostgresErrorCodes.CheckViolation);
    }

    [Fact]
    public async Task UniqueIndexes_CompletionPerItemDate_AndOverridePerDate()
    {
        var (_, item, _) = await SeedOwnersAsync();
        var date = new DateOnly(2026, 10, 5);

        await using (var db = NewDb())
        {
            db.ChecklistCompletions.Add(new ChecklistCompletion { ChecklistItemId = item.Id, OccurrenceDate = date });
            db.ChecklistCompletions.Add(new ChecklistCompletion { ChecklistItemId = item.Id, OccurrenceDate = date });
            var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        }

        await using (var db = NewDb())
        {
            db.DayOverrides.Add(new DayOverride { Date = date });
            db.DayOverrides.Add(new DayOverride { Date = date, Mode = DayOverrideMode.RestDay });
            var ex = InnerPostgres(await Assert.ThrowsAnyAsync<Exception>(() => db.SaveChangesAsync()));
            Assert.Equal(PostgresErrorCodes.UniqueViolation, ex.SqlState);
        }
    }

    [Fact]
    public async Task DeletingChecklistViaApi_CascadesItemsCompletionsItemReminders_AndNullsSoftLinks()
    {
        Guid checklistId, itemId, blockId, futureTaskId, templateId;
        await using (var db = NewDb())
        {
            var checklist = new Checklist { Name = "Doomed" };
            var item = new ChecklistItem { ChecklistId = checklist.Id, Title = "Item" };
            var template = new TimetableTemplate { Name = "T" };
            var block = new TimetableBlock { TemplateId = template.Id, Title = "B", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0), ChecklistId = checklist.Id };
            var task = new FutureTask { Title = "Promoted", DueDate = new DateOnly(2026, 11, 1), PromoteToChecklistId = checklist.Id };
            db.AddRange(checklist, item, template, block, task);
            db.ChecklistCompletions.Add(new ChecklistCompletion { ChecklistItemId = item.Id, OccurrenceDate = new DateOnly(2026, 10, 1) });
            db.Reminders.Add(new Reminder { ChecklistItemId = item.Id, OffsetMinutes = 10, FireAtUtc = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            (checklistId, itemId, blockId, futureTaskId, templateId) = (checklist.Id, item.Id, block.Id, task.Id, template.Id);
        }

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"/api/v1/checklists/{checklistId}"));

        await using (var db = NewDb())
        {
            Assert.False(await db.ChecklistItems.AnyAsync(i => i.Id == itemId));
            Assert.False(await db.ChecklistCompletions.AnyAsync());
            Assert.False(await db.Reminders.AnyAsync());
            Assert.Null((await db.TimetableBlocks.SingleAsync(b => b.Id == blockId)).ChecklistId);
            Assert.Null((await db.FutureTasks.SingleAsync(t => t.Id == futureTaskId)).PromoteToChecklistId);
            Assert.True(await db.TimetableTemplates.AnyAsync(t => t.Id == templateId));
        }
    }

    [Fact]
    public async Task DeletingTemplateViaApi_CascadesBlocksAssignments_AndNullsOverrideAndItemLinks()
    {
        Guid templateId, itemId;
        var date = new DateOnly(2026, 10, 10);
        await using (var db = NewDb())
        {
            var template = new TimetableTemplate { Name = "Doomed" };
            var block = new TimetableBlock { TemplateId = template.Id, Title = "B", StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0) };
            var checklist = new Checklist { Name = "C" };
            var item = new ChecklistItem { ChecklistId = checklist.Id, Title = "Linked", AnchorType = AnchorType.LinkedToBlock, TimetableBlockId = block.Id };
            db.AddRange(template, block, checklist, item);
            db.TimetableAssignments.Add(new TimetableAssignment { TemplateId = template.Id, Scope = AssignmentScope.Weekday, DayOfWeek = DayOfWeek.Monday });
            db.DayOverrides.Add(new DayOverride { Date = date, Mode = DayOverrideMode.UseTemplate, TemplateId = template.Id });
            await db.SaveChangesAsync();
            (templateId, itemId) = (template.Id, item.Id);
        }

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"/api/v1/timetable/templates/{templateId}"));

        await using (var db = NewDb())
        {
            Assert.False(await db.TimetableBlocks.AnyAsync());
            Assert.False(await db.TimetableAssignments.AnyAsync());
            Assert.Null((await db.DayOverrides.SingleAsync()).TemplateId);
            Assert.Null((await db.ChecklistItems.SingleAsync(i => i.Id == itemId)).TimetableBlockId);
        }
    }

    [Fact]
    public async Task DeletingFutureTaskViaApi_CascadesReminders_AndKeepsNotificationHistory()
    {
        Guid taskId, notificationId;
        await using (var db = NewDb())
        {
            var task = new FutureTask { Title = "Doomed", DueDate = new DateOnly(2026, 11, 1) };
            var reminder = new Reminder { FutureTaskId = task.Id, OffsetMinutes = 0, FireAtUtc = DateTimeOffset.UtcNow, Status = ReminderStatus.Sent };
            var log = new NotificationLog { ReminderId = reminder.Id, Title = "Fired", Body = "b" };
            db.AddRange(task, reminder, log);
            await db.SaveChangesAsync();
            (taskId, notificationId) = (task.Id, log.Id);
        }

        await AssertStatusAsync(HttpStatusCode.NoContent, await Client.DeleteAsync($"/api/v1/future-tasks/{taskId}"));

        await using (var db = NewDb())
        {
            Assert.False(await db.Reminders.AnyAsync());
            var log = await db.NotificationLogs.SingleAsync(n => n.Id == notificationId);
            Assert.Null(log.ReminderId);
        }
    }

    [Fact]
    public async Task ChecklistItemInsertedWithColumnDefaults_IsReadableByTheApp()
    {
        // Hand-written SQL (seed scripts, manual fixes) relies on init.sql's column defaults,
        // including the recurrence jsonb default — the app must be able to read such a row.
        var checklistId = Guid.NewGuid();
        await using (var connection = new NpgsqlConnection(Fx.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "INSERT INTO checklists (id, name) VALUES (@id, 'Raw'); INSERT INTO checklist_items (checklist_id, title, due_date) VALUES (@id, 'Raw item', @due);",
                connection);
            cmd.Parameters.AddWithValue("id", checklistId);
            cmd.Parameters.AddWithValue("due", Today);
            await cmd.ExecuteNonQueryAsync();
        }

        var items = await GetOkAsync($"/api/v1/checklists/{checklistId}/items");
        var item = Assert.Single(items.EnumerateArray());
        Assert.Equal("None", item.GetProperty("recurrence").GetProperty("type").GetString());
        Assert.True(item.GetProperty("isActive").GetBoolean());

        var today = await GetOkAsync("/api/v1/today");
        Assert.Contains(today.GetProperty("checklists").EnumerateArray(),
            g => g.GetProperty("checklistId").GetGuid() == checklistId);
    }
}
