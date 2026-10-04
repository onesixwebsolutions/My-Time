using DayGrid.Domain.Entities;
using DayGrid.Domain.Enums;
using DayGrid.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace DayGrid.Infrastructure.Data;

/// <summary>
/// Dev-only seed data — two templates, three checklists with items spanning every anchor type
/// and several recurrence types, a handful of future tasks with reminders, and a few standalone
/// simple tasks. Called from Program.cs only when <c>IsDevelopment()</c> and the database has no accounts yet. Flavour text mirrors
/// the plan and the HTML mockup (DayGrid-Mockup.html) so screenshots line up with real data.
/// </summary>
public static class SeedData
{
    public static void Seed(AppDbContext db)
    {
        // Idempotent: never overwrite existing data, whether that's a previous seed run or
        // real usage — and never once an account exists. Rows are inserted unowned (legacy), so
        // the bootstrap admin claims them when it confirms its email (see BootstrapAdminPolicy). Pass a system (unfiltered) context.
        if (db.Users.Any()
            || db.Checklists.IgnoreQueryFilters().Any()
            || db.TimetableTemplates.IgnoreQueryFilters().Any())
            return;

        var now = DateTimeOffset.UtcNow;
        var recurrenceStart = new DateOnly(2026, 8, 1);

        // ---------------------------------------------------------------
        // Checklists (created first so templates/blocks can link to their ids)
        // ---------------------------------------------------------------
        var morningRoutine = new Checklist
        {
            Name = "Morning Routine",
            Description = "The first hour, on autopilot.",
            Color = "#f59e0b",
            Icon = "sunrise",
            SortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        var work = new Checklist
        {
            Name = "Work",
            Description = "Recurring work hygiene — not project tasks.",
            Color = "#3b82f6",
            Icon = "briefcase",
            SortOrder = 1,
            CreatedAt = now,
            UpdatedAt = now
        };
        var eveningWindDown = new Checklist
        {
            Name = "Evening Wind-down",
            Description = "Close the day out deliberately.",
            Color = "#6366f1",
            Icon = "moon",
            SortOrder = 2,
            CreatedAt = now,
            UpdatedAt = now
        };

        // ---------------------------------------------------------------
        // Timetable templates + blocks
        // ---------------------------------------------------------------
        var weekday = new TimetableTemplate
        {
            Name = "Weekday",
            Description = "Mon–Fri default shape.",
            IsDefault = true,
            DayStart = new TimeOnly(6, 0),
            DayEnd = new TimeOnly(23, 0),
            SlotMinutes = 30,
            CreatedAt = now,
            UpdatedAt = now
        };
        var weekend = new TimetableTemplate
        {
            Name = "Weekend",
            Description = "Slower mornings, more free time.",
            IsDefault = false,
            DayStart = new TimeOnly(7, 0),
            DayEnd = new TimeOnly(23, 0),
            SlotMinutes = 30,
            CreatedAt = now,
            UpdatedAt = now
        };

        var workoutBlock = new TimetableBlock
        {
            TemplateId = weekday.Id,
            Title = "Workout",
            StartTime = new TimeOnly(6, 0),
            EndTime = new TimeOnly(7, 0),
            Category = BlockCategory.Health,
            Color = "#22c55e",
            Location = "Home gym",
            SortOrder = 0
        };
        var deepWorkBlock = new TimetableBlock
        {
            TemplateId = weekday.Id,
            Title = "Deep Work — Project Alpha",
            StartTime = new TimeOnly(9, 0),
            EndTime = new TimeOnly(12, 30),
            Category = BlockCategory.Work,
            Color = "#3b82f6",
            ChecklistId = work.Id,
            SortOrder = 3
        };

        var weekdayBlocks = new List<TimetableBlock>
        {
            workoutBlock,
            new()
            {
                TemplateId = weekday.Id,
                Title = "Breakfast + Reading",
                StartTime = new TimeOnly(7, 0),
                EndTime = new TimeOnly(8, 0),
                Category = BlockCategory.Personal,
                SortOrder = 1
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Commute",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(9, 0),
                Category = BlockCategory.Other,
                SortOrder = 2
            },
            deepWorkBlock,
            new()
            {
                TemplateId = weekday.Id,
                Title = "Lunch",
                StartTime = new TimeOnly(12, 30),
                EndTime = new TimeOnly(13, 0),
                Category = BlockCategory.Break,
                SortOrder = 4
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Meetings & Reviews",
                StartTime = new TimeOnly(13, 0),
                EndTime = new TimeOnly(15, 0),
                Category = BlockCategory.Work,
                Location = "Desk",
                SortOrder = 5
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Admin & Wrap-up",
                StartTime = new TimeOnly(15, 0),
                EndTime = new TimeOnly(16, 30),
                Category = BlockCategory.Work,
                SortOrder = 6
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Dinner",
                StartTime = new TimeOnly(18, 0),
                EndTime = new TimeOnly(19, 0),
                Category = BlockCategory.Personal,
                SortOrder = 7
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Evening Wind-down",
                StartTime = new TimeOnly(21, 0),
                EndTime = new TimeOnly(22, 0),
                Category = BlockCategory.Break,
                ChecklistId = eveningWindDown.Id,
                SortOrder = 8
            },
            new()
            {
                TemplateId = weekday.Id,
                Title = "Sleep",
                StartTime = new TimeOnly(22, 30),
                EndTime = new TimeOnly(23, 0),
                Category = BlockCategory.Sleep,
                SortOrder = 9
            }
        };

        var weekendBlocks = new List<TimetableBlock>
        {
            new()
            {
                TemplateId = weekend.Id,
                Title = "Slow Morning",
                StartTime = new TimeOnly(7, 30),
                EndTime = new TimeOnly(8, 30),
                Category = BlockCategory.Personal,
                SortOrder = 0
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Chores & Errands",
                StartTime = new TimeOnly(9, 30),
                EndTime = new TimeOnly(12, 0),
                Category = BlockCategory.Other,
                SortOrder = 1
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Lunch",
                StartTime = new TimeOnly(12, 0),
                EndTime = new TimeOnly(13, 0),
                Category = BlockCategory.Break,
                SortOrder = 2
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Free Time / Hobbies",
                StartTime = new TimeOnly(13, 0),
                EndTime = new TimeOnly(17, 0),
                Category = BlockCategory.Personal,
                SortOrder = 3
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Exercise",
                StartTime = new TimeOnly(17, 0),
                EndTime = new TimeOnly(18, 0),
                Category = BlockCategory.Health,
                Color = "#22c55e",
                SortOrder = 4
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Dinner & Family Time",
                StartTime = new TimeOnly(18, 30),
                EndTime = new TimeOnly(21, 30),
                Category = BlockCategory.Personal,
                SortOrder = 5
            },
            new()
            {
                TemplateId = weekend.Id,
                Title = "Wind-down",
                StartTime = new TimeOnly(22, 0),
                EndTime = new TimeOnly(23, 0),
                Category = BlockCategory.Break,
                SortOrder = 6
            }
        };

        var assignments = new List<TimetableAssignment>();
        foreach (var dow in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday })
        {
            assignments.Add(new TimetableAssignment
            {
                TemplateId = weekday.Id,
                Scope = AssignmentScope.Weekday,
                DayOfWeek = dow
            });
        }
        foreach (var dow in new[] { DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            assignments.Add(new TimetableAssignment
            {
                TemplateId = weekend.Id,
                Scope = AssignmentScope.Weekday,
                DayOfWeek = dow
            });
        }

        // ---------------------------------------------------------------
        // Checklist items — every AnchorType and several RecurrenceTypes represented.
        // ---------------------------------------------------------------
        var items = new List<ChecklistItem>
        {
            // Morning Routine
            new()
            {
                ChecklistId = morningRoutine.Id,
                Title = "Take vitamins",
                Priority = Priority.Normal,
                EstimatedMinutes = 2,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(7, 15),
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                ReminderOffsetMinutes = 5,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = morningRoutine.Id,
                Title = "Make bed",
                Priority = Priority.Low,
                AnchorType = AnchorType.Anytime,
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = morningRoutine.Id,
                Title = "Morning journal",
                Priority = Priority.Normal,
                EstimatedMinutes = 10,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(7, 40),
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = morningRoutine.Id,
                Title = "Stretch 10 min",
                Priority = Priority.Normal,
                EstimatedMinutes = 10,
                AnchorType = AnchorType.LinkedToBlock,
                TimetableBlockId = workoutBlock.Id,
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                SortOrder = 3,
                CreatedAt = now,
                UpdatedAt = now
            },

            // Work
            new()
            {
                ChecklistId = work.Id,
                Title = "Review PR queue",
                Notes = "Anything older than 24h gets a comment",
                Priority = Priority.High,
                EstimatedMinutes = 20,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(16, 30),
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.Weekly,
                    Interval = 1,
                    DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                    StartDate = recurrenceStart
                },
                ReminderOffsetMinutes = 10,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = work.Id,
                Title = "Standup notes",
                Priority = Priority.Normal,
                EstimatedMinutes = 5,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(9, 30),
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.Weekly,
                    Interval = 1,
                    DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                    StartDate = recurrenceStart
                },
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = work.Id,
                Title = "Review inbox",
                Priority = Priority.Normal,
                AnchorType = AnchorType.TimeWindow,
                WindowStart = new TimeOnly(9, 0),
                WindowEnd = new TimeOnly(9, 30),
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.Weekly,
                    Interval = 1,
                    DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday },
                    StartDate = recurrenceStart
                },
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = work.Id,
                Title = "Weekly planning",
                Priority = Priority.High,
                EstimatedMinutes = 30,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(9, 0),
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.Weekly,
                    Interval = 1,
                    DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Monday },
                    StartDate = recurrenceStart
                },
                SortOrder = 3,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = work.Id,
                Title = "Expense report",
                Priority = Priority.Low,
                AnchorType = AnchorType.Anytime,
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.MonthlyByDay,
                    DayOfMonth = 28,
                    StartDate = recurrenceStart
                },
                SortOrder = 4,
                CreatedAt = now,
                UpdatedAt = now
            },

            // Evening Wind-down
            new()
            {
                ChecklistId = eveningWindDown.Id,
                Title = "Prep clothes for tomorrow",
                Priority = Priority.Low,
                EstimatedMinutes = 5,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(21, 30),
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.Weekly,
                    Interval = 1,
                    DaysOfWeek = new List<DayOfWeek> { DayOfWeek.Sunday, DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday },
                    StartDate = recurrenceStart
                },
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = eveningWindDown.Id,
                Title = "Read 20 minutes",
                Priority = Priority.Normal,
                EstimatedMinutes = 20,
                AnchorType = AnchorType.Anytime,
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = eveningWindDown.Id,
                Title = "Screens off",
                Priority = Priority.Normal,
                AnchorType = AnchorType.FixedTime,
                AnchorTime = new TimeOnly(22, 0),
                Recurrence = new RecurrenceRule { Type = RecurrenceType.Daily, StartDate = recurrenceStart },
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = eveningWindDown.Id,
                Title = "Water plants",
                Priority = Priority.Low,
                AnchorType = AnchorType.Anytime,
                Recurrence = new RecurrenceRule { Type = RecurrenceType.EveryNDays, Interval = 3, StartDate = recurrenceStart },
                SortOrder = 3,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                ChecklistId = eveningWindDown.Id,
                Title = "Deep clean",
                Notes = "Bathroom + kitchen deep clean",
                Priority = Priority.Normal,
                EstimatedMinutes = 60,
                AnchorType = AnchorType.Anytime,
                Recurrence = new RecurrenceRule
                {
                    Type = RecurrenceType.MonthlyByWeekday,
                    NthWeekday = new NthWeekday(1, DayOfWeek.Sunday),
                    StartDate = recurrenceStart
                },
                SortOrder = 4,
                CreatedAt = now,
                UpdatedAt = now
            }
        };

        // ---------------------------------------------------------------
        // Future tasks + reminders
        // ---------------------------------------------------------------
        var renewPassport = new FutureTask
        {
            Title = "Renew passport",
            Notes = "Appointment slot booked at PSK",
            DueDate = new DateOnly(2026, 9, 12),
            DueTime = new TimeOnly(11, 0),
            Category = "Personal",
            Priority = Priority.Critical,
            Status = FutureTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        renewPassport.Reminders = new List<Reminder>
        {
            new()
            {
                FutureTaskId = renewPassport.Id,
                OffsetMinutes = 10080,
                Channels = NotificationChannel.InApp | NotificationChannel.Email,
                FireAtUtc = renewPassport.DueDate.ToDateTime(renewPassport.DueTime!.Value).AddMinutes(-10080).ToUniversalTime()
            },
            new()
            {
                FutureTaskId = renewPassport.Id,
                OffsetMinutes = 1440,
                Channels = NotificationChannel.InApp | NotificationChannel.Email,
                FireAtUtc = renewPassport.DueDate.ToDateTime(renewPassport.DueTime!.Value).AddMinutes(-1440).ToUniversalTime()
            },
            new()
            {
                FutureTaskId = renewPassport.Id,
                OffsetMinutes = 30,
                Channels = NotificationChannel.InApp,
                FireAtUtc = renewPassport.DueDate.ToDateTime(renewPassport.DueTime!.Value).AddMinutes(-30).ToUniversalTime()
            }
        };

        var dentist = new FutureTask
        {
            Title = "Call the dentist to reschedule",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3),
            DueTime = null,
            Category = "Personal",
            Priority = Priority.Normal,
            Status = FutureTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        dentist.Reminders = new List<Reminder>
        {
            new()
            {
                FutureTaskId = dentist.Id,
                OffsetMinutes = 540, // 9h before "end of day" — all-day tasks fire relative to 00:00 due date
                Channels = NotificationChannel.InApp,
                FireAtUtc = dentist.DueDate.ToDateTime(new TimeOnly(9, 0)).ToUniversalTime()
            }
        };

        var quarterlyReport = new FutureTask
        {
            Title = "Submit quarterly report",
            Notes = "Attach the Q3 metrics export",
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(10),
            DueTime = new TimeOnly(17, 0),
            Category = "Work",
            Priority = Priority.High,
            Status = FutureTaskStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        quarterlyReport.Reminders = new List<Reminder>
        {
            new()
            {
                FutureTaskId = quarterlyReport.Id,
                OffsetMinutes = 1440,
                Channels = NotificationChannel.InApp | NotificationChannel.Email,
                FireAtUtc = quarterlyReport.DueDate.ToDateTime(quarterlyReport.DueTime!.Value).AddMinutes(-1440).ToUniversalTime()
            }
        };

        var futureTasks = new List<FutureTask> { renewPassport, dentist, quarterlyReport };

        // ---------------------------------------------------------------
        // Standalone simple tasks — never referenced by anything above, on purpose.
        // ---------------------------------------------------------------
        var simpleTasks = new List<SimpleTask>
        {
            new()
            {
                Title = "Read \"Atomic Habits\"",
                Priority = Priority.Low,
                SortOrder = 0,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Title = "Fix squeaky door hinge",
                Priority = Priority.Normal,
                SortOrder = 1,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Title = "Research standing desks",
                Priority = Priority.Low,
                SortOrder = 2,
                CreatedAt = now,
                UpdatedAt = now
            },
            new()
            {
                Title = "Plan weekend trip",
                Notes = "Short list: hills or coast",
                Priority = Priority.Normal,
                Status = SimpleTaskStatus.Done,
                SortOrder = 3,
                CompletedAt = now.AddDays(-1),
                CreatedAt = now.AddDays(-4),
                UpdatedAt = now.AddDays(-1)
            }
        };

        db.Checklists.AddRange(morningRoutine, work, eveningWindDown);
        db.TimetableTemplates.AddRange(weekday, weekend);
        db.TimetableBlocks.AddRange(weekdayBlocks);
        db.TimetableBlocks.AddRange(weekendBlocks);
        db.TimetableAssignments.AddRange(assignments);
        db.ChecklistItems.AddRange(items);
        db.FutureTasks.AddRange(futureTasks);
        db.SimpleTasks.AddRange(simpleTasks);

        db.SaveChanges();
    }
}
