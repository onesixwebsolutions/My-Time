using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;
using DayGrid.Infrastructure.Data;
using DayGrid.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Xunit;

namespace DayGrid.IntegrationTests.Schema;

/// <summary>
/// The schema is hand-written (db/init.sql), not generated from the EF model, so nothing but
/// these tests keeps the two in step.
/// </summary>
public class SchemaParityTests : IntegrationTestBase
{
    public SchemaParityTests(PostgresFixture fixture) : base(fixture) { }

    private sealed record DbColumn(string Table, string Column, string DataType, bool Nullable, int? MaxLength, int? Precision, int? Scale);

    [Fact]
    public async Task EfModel_Matches_InformationSchemaColumns()
    {
        var dbColumns = new Dictionary<(string, string), DbColumn>();
        await using (var connection = new NpgsqlConnection(Fx.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT table_name, column_name, data_type, is_nullable = 'YES',
                       character_maximum_length, numeric_precision, numeric_scale
                FROM information_schema.columns
                WHERE table_schema = 'public'
                """, connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var col = new DbColumn(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6));
                dbColumns[(col.Table, col.Column)] = col;
            }
        }

        await using var db = NewDb();
        var diffs = new List<string>();
        var mapped = new HashSet<(string, string)>();
        var dbTables = dbColumns.Keys.Select(k => k.Item1).ToHashSet();

        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName()!;
            if (!dbTables.Contains(table))
            {
                diffs.Add($"{entityType.ClrType.Name}: table '{table}' does not exist in init.sql");
                continue;
            }
            var store = StoreObjectIdentifier.Table(table, entityType.GetSchema());

            foreach (var property in entityType.GetProperties())
            {
                var column = property.GetColumnName(store)!;
                var where = $"{table}.{column} ({entityType.ClrType.Name}.{property.Name})";
                mapped.Add((table, column));

                if (!dbColumns.TryGetValue((table, column), out var dbCol))
                {
                    diffs.Add($"{where}: column missing in database");
                    continue;
                }

                var efType = property.GetColumnType();
                var efFamily = TypeFamily(efType);
                var dbFamily = TypeFamily(dbCol.DataType);
                if (efFamily != dbFamily)
                    diffs.Add($"{where}: type family differs — EF '{efType}' ({efFamily}) vs DB '{dbCol.DataType}' ({dbFamily})");

                var efNullable = property.IsColumnNullable(store);
                if (efNullable != dbCol.Nullable)
                    diffs.Add($"{where}: nullability differs — EF {(efNullable ? "NULL" : "NOT NULL")} vs DB {(dbCol.Nullable ? "NULL" : "NOT NULL")}");

                if (dbCol.DataType == "character varying" && property.GetMaxLength() != dbCol.MaxLength)
                    diffs.Add($"{where}: max length differs — EF {property.GetMaxLength()?.ToString() ?? "(none)"} vs DB {dbCol.MaxLength}");

                if (dbCol.DataType == "numeric")
                {
                    var m = Regex.Match(efType, @"numeric\((\d+),(\d+)\)");
                    if (!m.Success || int.Parse(m.Groups[1].Value) != dbCol.Precision || int.Parse(m.Groups[2].Value) != dbCol.Scale)
                        diffs.Add($"{where}: numeric precision differs — EF '{efType}' vs DB numeric({dbCol.Precision},{dbCol.Scale})");
                }
            }
        }

        foreach (var key in dbColumns.Keys.Where(k => !mapped.Contains(k)))
            diffs.Add($"{key.Item1}.{key.Item2}: column exists in database but is not mapped by EF");

        Assert.True(diffs.Count == 0, "EF model vs init.sql differences:\n" + string.Join("\n", diffs.OrderBy(d => d)));
    }

    [Fact]
    public async Task EfForeignKeys_Match_DatabaseForeignKeysAndDeleteRules()
    {
        var dbFks = new Dictionary<(string Table, string Column), (string RefTable, string DeleteRule)>();
        await using (var connection = new NpgsqlConnection(Fx.ConnectionString))
        {
            await connection.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                """
                SELECT kcu.table_name, kcu.column_name, ccu.table_name, rc.delete_rule
                FROM information_schema.referential_constraints rc
                JOIN information_schema.key_column_usage kcu ON kcu.constraint_name = rc.constraint_name AND kcu.constraint_schema = rc.constraint_schema
                JOIN information_schema.constraint_column_usage ccu ON ccu.constraint_name = rc.unique_constraint_name AND ccu.constraint_schema = rc.unique_constraint_schema
                WHERE rc.constraint_schema = 'public'
                """, connection);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                dbFks[(reader.GetString(0), reader.GetString(1))] = (reader.GetString(2), reader.GetString(3));
        }

        await using var db = NewDb();
        var diffs = new List<string>();
        var efFkColumns = new HashSet<(string, string)>();
        foreach (var entityType in db.Model.GetEntityTypes())
        {
            var table = entityType.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, null);
            foreach (var fk in entityType.GetForeignKeys())
            {
                var column = fk.Properties.Single().GetColumnName(store)!;
                efFkColumns.Add((table, column));
                var principalTable = fk.PrincipalEntityType.GetTableName();
                var expectedRule = fk.DeleteBehavior switch
                {
                    DeleteBehavior.Cascade => "CASCADE",
                    DeleteBehavior.SetNull => "SET NULL",
                    _ => "NO ACTION"
                };
                if (!dbFks.TryGetValue((table, column), out var dbFk))
                    diffs.Add($"{table}.{column}: EF FK to {principalTable} has no database FK");
                else if (dbFk.RefTable != principalTable || dbFk.DeleteRule != expectedRule)
                    diffs.Add($"{table}.{column}: EF -> {principalTable} ON DELETE {expectedRule}, DB -> {dbFk.RefTable} ON DELETE {dbFk.DeleteRule}");
            }
        }

        // A database FK that EF does not know about means EF cannot order inserts/deletes around it
        // or fix up tracked entities when the database applies ON DELETE SET NULL.
        foreach (var (key, fk) in dbFks.Where(f => !efFkColumns.Contains(f.Key)))
            diffs.Add($"{key.Table}.{key.Column}: database FK -> {fk.RefTable} ON DELETE {fk.DeleteRule} is not modelled in EF");

        Assert.True(diffs.Count == 0, "Foreign key differences:\n" + string.Join("\n", diffs));
    }

    [Fact]
    public async Task EfCheckConstraintsAndUniqueIndexes_ExistInDatabase()
    {
        var constraints = new HashSet<string>();
        var uniqueIndexes = new HashSet<string>();
        await using (var connection = new NpgsqlConnection(Fx.ConnectionString))
        {
            await connection.OpenAsync();
            await using (var cmd = new NpgsqlCommand("SELECT conname FROM pg_constraint WHERE contype = 'c'", connection))
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) constraints.Add(reader.GetString(0));
            await using (var cmd = new NpgsqlCommand(
                "SELECT tablename || '(' || regexp_replace(indexdef, '^.*\\((.*)\\)$', '\\1') || ')' FROM pg_indexes WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE INDEX%'", connection))
            await using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync()) uniqueIndexes.Add(reader.GetString(0));
        }

        await using var db = NewDb();
        var missing = new List<string>();
        foreach (var entityType in db.GetService<IDesignTimeModel>().Model.GetEntityTypes())
        {
            foreach (var check in entityType.GetCheckConstraints())
                if (!constraints.Contains(check.Name!))
                    missing.Add($"check constraint {check.Name}");

            var table = entityType.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, null);
            foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique))
            {
                var key = $"{table}({string.Join(", ", index.Properties.Select(p => p.GetColumnName(store)))})";
                if (!uniqueIndexes.Contains(key))
                    missing.Add($"unique index {key}");
            }
        }

        Assert.True(missing.Count == 0, "Missing in database: " + string.Join(", ", missing));
    }

    [Fact]
    public async Task EveryEntityType_FullyPopulatedRow_RoundTripsThroughPostgres()
    {
        var rows = BuildFullyPopulatedRows();
        // Snapshot what we intend to write: SaveChanges copies database-generated values (RETURNING)
        // back into the instances, which would hide a value the database silently replaced.
        var written = new Dictionary<object, Dictionary<string, object?>>(ReferenceEqualityComparer.Instance);

        await using (var db = NewDb())
        {
            var modelTypes = db.Model.GetEntityTypes().Select(t => t.ClrType).ToHashSet();
            var covered = rows.Select(r => r.GetType()).ToHashSet();
            Assert.True(modelTypes.SetEquals(covered),
                "Round-trip coverage gap: " + string.Join(", ", modelTypes.Except(covered).Select(t => t.Name)));

            foreach (var row in rows)
                written[row] = db.Model.FindEntityType(row.GetType())!.GetProperties()
                    .Where(p => p.PropertyInfo is not null)
                    .ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(row));

            // The settings singleton (id = 1) is seeded by init.sql — replace it.
            await db.AppSettings.Where(s => s.Id == 1).ExecuteDeleteAsync();
            foreach (var row in rows)
                db.Add(row);
            await db.SaveChangesAsync();
        }

        var diffs = new List<string>();
        await using (var db = NewDb())
        {
            foreach (var expected in rows)
            {
                var entityType = db.Model.FindEntityType(expected.GetType())!;
                var key = entityType.FindPrimaryKey()!.Properties.Single().PropertyInfo!.GetValue(expected)!;
                var actual = await db.FindAsync(expected.GetType(), key);
                if (actual is null)
                {
                    diffs.Add($"{expected.GetType().Name}: row not found after insert");
                    continue;
                }

                foreach (var property in entityType.GetProperties().Where(p => p.PropertyInfo is not null))
                {
                    var e = written[expected][property.Name];
                    var a = property.PropertyInfo!.GetValue(actual);
                    if (!ValuesEqual(e, a))
                        diffs.Add($"{expected.GetType().Name}.{property.Name}: wrote {Describe(e)}, read back {Describe(a)}");
                }
            }
        }

        Assert.True(diffs.Count == 0, "Round-trip differences:\n" + string.Join("\n", diffs));
    }

    private static List<object> BuildFullyPopulatedRows()
    {
        static string Max(int length, string seed = "Ünïcødé ✓ 漢字 ") =>
            string.Concat(Enumerable.Repeat(seed, length / seed.Length + 1))[..length];

        var t0 = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).AddTicks(1230); // 123 µs

        var checklist = new Checklist
        {
            Name = Max(120), Description = Max(5000), Color = "#1a2b3c4d", Icon = Max(40),
            SortOrder = int.MaxValue, IsArchived = true, CreatedAt = t0, UpdatedAt = t0.AddDays(1)
        };
        var template = new TimetableTemplate
        {
            Name = Max(120), Description = Max(300), IsDefault = true,
            DayStart = new TimeOnly(0, 0), DayEnd = new TimeOnly(23, 59, 59), SlotMinutes = 15,
            CreatedAt = t0, UpdatedAt = t0
        };
        var block = new TimetableBlock
        {
            TemplateId = template.Id, Title = Max(160), StartTime = new TimeOnly(0, 0), EndTime = new TimeOnly(23, 59, 59),
            Category = BlockCategory.Sleep, Color = "#ffffffff", Location = Max(120), ChecklistId = checklist.Id,
            AllowOverlap = true, NotifyAtStart = true, SortOrder = -5
        };
        var item = new ChecklistItem
        {
            ChecklistId = checklist.Id, Title = Max(200), Notes = Max(2000), Priority = Priority.Critical,
            EstimatedMinutes = 1440, AnchorType = AnchorType.LinkedToBlock, AnchorTime = new TimeOnly(7, 15),
            WindowStart = new TimeOnly(6, 0), WindowEnd = new TimeOnly(9, 30), TimetableBlockId = block.Id,
            Recurrence = new RecurrenceRule
            {
                Type = RecurrenceType.MonthlyByWeekday, Interval = 2,
                DaysOfWeek = new() { DayOfWeek.Monday, DayOfWeek.Friday }, DayOfMonth = 31,
                NthWeekday = new NthWeekday(-1, DayOfWeek.Friday),
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2027, 12, 31),
                ExceptionDates = new() { new DateOnly(2026, 12, 25) }
            },
            DueDate = new DateOnly(2026, 2, 28), ReminderOffsetMinutes = 10080, IsActive = false, SortOrder = 7,
            CreatedAt = t0, UpdatedAt = t0
        };
        var completion = new ChecklistCompletion
        {
            ChecklistItemId = item.Id, OccurrenceDate = new DateOnly(2026, 2, 28), CompletedAt = t0,
            Status = CompletionStatus.Partial, Note = Max(1000)
        };
        var assignment = new TimetableAssignment
        {
            TemplateId = template.Id, Scope = AssignmentScope.DateRange, DayOfWeek = DayOfWeek.Saturday,
            DateFrom = new DateOnly(2026, 1, 1), DateTo = new DateOnly(2026, 12, 31), Priority = 9
        };
        var dayOverride = new DayOverride
        {
            Date = new DateOnly(2026, 7, 4), Mode = DayOverrideMode.CustomOnly, TemplateId = template.Id, Note = Max(200)
        };
        var futureTask = new FutureTask
        {
            Title = Max(200), Notes = Max(800), DueDate = new DateOnly(2030, 1, 31), DueTime = new TimeOnly(23, 45),
            Category = Max(60), Priority = Priority.High, Status = FutureTaskStatus.Deferred, CompletedAt = t0,
            PromoteToChecklistId = checklist.Id, CreatedAt = t0, UpdatedAt = t0
        };
        var reminder = new Reminder
        {
            FutureTaskId = futureTask.Id, OffsetMinutes = 20160, FireAtUtc = t0.AddDays(3),
            Channels = NotificationChannel.InApp | NotificationChannel.Email, Status = ReminderStatus.Failed,
            SentAtUtc = t0, AttemptCount = 3
        };
        var itemReminder = new Reminder
        {
            ChecklistItemId = item.Id, OffsetMinutes = 0, FireAtUtc = t0, Channels = NotificationChannel.Email,
            Status = ReminderStatus.Cancelled, AttemptCount = 1
        };
        var notification = new NotificationLog
        {
            ReminderId = reminder.Id, Title = Max(200), Body = Max(4000), Channel = NotificationChannel.Email,
            CreatedAtUtc = t0, ReadAtUtc = t0.AddMinutes(1), Error = Max(500)
        };
        var settings = new AppSetting
        {
            Id = 1, TimeZone = Max(60), WeekStartsOn = DayOfWeek.Sunday, DayStart = new TimeOnly(0, 0),
            DayEnd = new TimeOnly(0, 30), DefaultSlotMinutes = 60, EmailEnabled = false, EmailTo = Max(200),
            DailyDigestTime = null, Theme = Max(20)
        };
        var simpleTask = new SimpleTask
        {
            Title = Max(200), Notes = Max(300), Priority = Priority.Low, Status = SimpleTaskStatus.Done,
            SortOrder = int.MinValue, CompletedAt = t0, CreatedAt = t0, UpdatedAt = t0
        };
        var constant = new ConstantExpense
        {
            Name = Max(160), Amount = 9_999_999_999.99m, Category = Max(60), DayOfMonth = 31, Notes = Max(100),
            IsActive = false, CreatedAt = t0, UpdatedAt = t0
        };
        var varying = new VaryingExpense
        {
            Title = Max(160), Amount = 0.01m, Category = Max(60), Date = new DateOnly(1999, 12, 31), Notes = Max(100),
            CreatedAt = t0, UpdatedAt = t0
        };
        var spend = new CompletedSpend
        {
            Title = Max(160), Amount = 0m, Category = Max(60), Date = new DateOnly(2100, 1, 1), Notes = Max(100),
            CreatedAt = t0, UpdatedAt = t0
        };

        return new List<object>
        {
            checklist, template, block, item, completion, assignment, dayOverride, futureTask, reminder,
            itemReminder, notification, settings, simpleTask, constant, varying, spend
        };
    }

    private static bool ValuesEqual(object? e, object? a) => (e, a) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        (DateTimeOffset x, DateTimeOffset y) => x.UtcTicks / 10 == y.UtcTicks / 10, // timestamptz = µs
        (RecurrenceRule x, RecurrenceRule y) => JsonSerializer.Serialize(x) == JsonSerializer.Serialize(y),
        _ => e.Equals(a)
    };

    private static string Describe(object? value) => value switch
    {
        null => "null",
        RecurrenceRule r => JsonSerializer.Serialize(r),
        string s when s.Length > 40 => $"\"{s[..40]}…\" (len {s.Length})",
        DateTimeOffset d => d.ToString("O"),
        _ => value.ToString() ?? "?"
    };

    private static string TypeFamily(string storeType)
    {
        var t = Regex.Replace(storeType.ToLowerInvariant(), @"\(.*\)", "").Trim();
        return t switch
        {
            "smallint" or "integer" or "bigint" or "int2" or "int4" or "int8" => "integer",
            "character varying" or "varchar" or "text" or "character" => "text",
            "boolean" or "bool" => "boolean",
            "uuid" => "uuid",
            "timestamp with time zone" or "timestamptz" => "timestamptz",
            "timestamp without time zone" or "timestamp" => "timestamp",
            "time without time zone" or "time" => "time",
            "date" => "date",
            "jsonb" or "json" => "json",
            "numeric" or "decimal" => "numeric",
            _ => t
        };
    }
}
