-- DayGrid — realistic sample data for local testing.
-- Run after db/init.sql. Uses psql \gset to thread generated UUIDs between statements,
-- so run this file with `psql`, not through a generic SQL driver that doesn't support it.
\set ON_ERROR_STOP on

-- ============================================================================
-- Checklists
-- ============================================================================
INSERT INTO checklists (name, description, color, icon, sort_order)
VALUES ('Morning Routine', 'The first hour, on autopilot.', '#f59e0b', 'sunrise', 0)
RETURNING id AS morning_id \gset

INSERT INTO checklists (name, description, color, icon, sort_order)
VALUES ('Work', 'Recurring work hygiene — not project tasks.', '#3b82f6', 'briefcase', 1)
RETURNING id AS work_id \gset

INSERT INTO checklists (name, description, color, icon, sort_order)
VALUES ('Evening Wind-down', 'Close the day out deliberately.', '#6366f1', 'moon', 2)
RETURNING id AS evening_id \gset

INSERT INTO checklists (name, description, color, icon, sort_order)
VALUES ('Home & Errands', 'Weekly and monthly household upkeep.', '#22c55e', 'home', 3)
RETURNING id AS home_id \gset

-- ============================================================================
-- Timetable templates
-- ============================================================================
INSERT INTO timetable_templates (name, description, is_default, day_start, day_end, slot_minutes)
VALUES ('Weekday', 'Mon–Fri default shape.', true, '06:00', '23:00', 30)
RETURNING id AS weekday_tpl_id \gset

INSERT INTO timetable_templates (name, description, is_default, day_start, day_end, slot_minutes)
VALUES ('Weekend', 'Slower mornings, more free time.', false, '07:00', '23:00', 30)
RETURNING id AS weekend_tpl_id \gset

-- ============================================================================
-- Weekday blocks (capture the two that checklist items link into)
-- ============================================================================
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, color, location, sort_order)
VALUES (:'weekday_tpl_id', 'Workout', '06:00', '07:00', 2, '#22c55e', 'Home gym', 0)
RETURNING id AS workout_block_id \gset

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Breakfast + Reading', '07:00', '08:00', 1, 1);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, location, sort_order)
VALUES (:'weekday_tpl_id', 'Commute', '08:00', '09:00', 4, 'Metro', 2);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, color, checklist_id, sort_order)
VALUES (:'weekday_tpl_id', 'Deep Work — Project Alpha', '09:00', '12:30', 0, '#3b82f6', :'work_id', 3)
RETURNING id AS deepwork_block_id \gset

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Lunch', '12:30', '13:15', 4, 4);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, location, sort_order)
VALUES (:'weekday_tpl_id', 'Meetings & Reviews', '13:15', '15:00', 0, 'Conf room', 5);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Learning — .NET / Angular', '15:00', '16:30', 3, 6);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Admin & Inbox', '16:30', '18:00', 0, 7);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Gym / Errands', '18:00', '19:00', 2, 8);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekday_tpl_id', 'Dinner + Family', '19:00', '20:00', 1, 9);

INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, color, checklist_id, sort_order)
VALUES (:'weekday_tpl_id', 'Wind-down & Journal', '20:00', '22:00', 1, '#6366f1', :'evening_id', 10)
RETURNING id AS winddown_block_id \gset

-- ============================================================================
-- Weekend blocks (simpler day)
-- ============================================================================
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekend_tpl_id', 'Slow Breakfast', '07:00', '08:30', 1, 0);
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, location, sort_order)
VALUES (:'weekend_tpl_id', 'Grocery Run', '09:00', '10:00', 6, 'Local market', 1);
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekend_tpl_id', 'Long Workout', '10:00', '11:30', 2, 2);
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekend_tpl_id', 'Free Time', '11:30', '18:00', 1, 3);
INSERT INTO timetable_blocks (template_id, title, start_time, end_time, category, sort_order)
VALUES (:'weekend_tpl_id', 'Family Dinner', '19:00', '20:30', 1, 4);

-- ============================================================================
-- Timetable assignments — most-specific-wins is evaluated by the app; these
-- rows just say "weekday scope, Mon–Fri => Weekday" / "weekday scope, Sat–Sun => Weekend".
-- day_of_week: 0=Sun..6=Sat, scope 0=Weekday(-rule)
-- ============================================================================
INSERT INTO timetable_assignments (template_id, scope, day_of_week, priority)
SELECT :'weekday_tpl_id', 0, d, 0 FROM unnest(ARRAY[1,2,3,4,5]) AS d;

INSERT INTO timetable_assignments (template_id, scope, day_of_week, priority)
SELECT :'weekend_tpl_id', 0, d, 0 FROM unnest(ARRAY[0,6]) AS d;

-- One date-specific override, a week out, just to exercise the resolver.
INSERT INTO day_overrides (date, mode, template_id, note)
VALUES (CURRENT_DATE + INTERVAL '9 day', 0, :'weekend_tpl_id', 'Travel day — lighter schedule');

-- ============================================================================
-- Checklist items — Morning Routine (all Daily/Weekly, all anchor types)
-- ============================================================================
INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, recurrence, estimated_minutes, reminder_offset_minutes, sort_order)
VALUES (:'morning_id', 'Take vitamins', 1, 1, '07:15',
  '{"type":1,"interval":1,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 2, 10, 0)
RETURNING id AS vitamins_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'morning_id', 'Make the bed', 0, 0,
  '{"type":1,"interval":1,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 1)
RETURNING id AS bed_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, timetable_block_id, recurrence, sort_order)
VALUES (:'morning_id', '30 min workout', 2, 3, :'workout_block_id',
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5,6],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 2)
RETURNING id AS workout_item_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, estimated_minutes, recurrence, sort_order)
VALUES (:'morning_id', 'Morning journal', 1, 1, '07:40', 10,
  '{"type":1,"interval":1,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 3);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, window_start, window_end, recurrence, sort_order)
VALUES (:'morning_id', 'Plan top 3 priorities', 1, 2, '08:00', '09:00',
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 4);

-- ============================================================================
-- Checklist items — Work (weekdays)
-- ============================================================================
INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, recurrence, sort_order)
VALUES (:'work_id', 'Check overnight deployments', 1, 1, '09:00',
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 0)
RETURNING id AS deploy_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, recurrence, sort_order)
VALUES (:'work_id', 'Triage support queue', 0, 1, '09:15',
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 1)
RETURNING id AS triage_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, recurrence, reminder_offset_minutes, sort_order)
VALUES (:'work_id', 'Send standup notes', 2, 1, '09:30',
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 5, 2);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, estimated_minutes, recurrence, reminder_offset_minutes, sort_order)
VALUES (:'work_id', 'Review PR queue', 2, 1, '16:30', 20,
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 10, 3);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, timetable_block_id, recurrence, sort_order)
VALUES (:'work_id', 'Update sprint board', 1, 3, (SELECT id FROM timetable_blocks WHERE title = 'Admin & Inbox' LIMIT 1),
  '{"type":2,"interval":1,"daysOfWeek":[1,2,3,4,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 4);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'work_id', 'Weekly report draft', 1, 0,
  '{"type":2,"interval":1,"daysOfWeek":[3],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 5);

-- ============================================================================
-- Checklist items — Evening Wind-down
-- ============================================================================
INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'evening_id', 'Water the plants', 0, 0,
  '{"type":5,"interval":3,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 0)
RETURNING id AS plants_id \gset

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, timetable_block_id, recurrence, sort_order)
VALUES (:'evening_id', 'Read 20 pages', 0, 3, :'winddown_block_id',
  '{"type":1,"interval":1,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 1);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, anchor_time, recurrence, sort_order)
VALUES (:'evening_id', 'Prep tomorrow''s clothes', 0, 1, '21:30',
  '{"type":2,"interval":1,"daysOfWeek":[0,1,2,3,4],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 2);

-- ============================================================================
-- Checklist items — Home & Errands (weekly/monthly)
-- ============================================================================
INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'home_id', 'Grocery run', 1, 0,
  '{"type":2,"interval":1,"daysOfWeek":[6],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 0);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'home_id', 'Deep clean', 1, 0,
  '{"type":4,"interval":1,"daysOfWeek":[],"dayOfMonth":null,"nthWeekday":{"nth":1,"dayOfWeek":0},"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 1);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'home_id', 'Pay rent', 2, 0,
  '{"type":3,"interval":1,"daysOfWeek":[],"dayOfMonth":5,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 2);

INSERT INTO checklist_items (checklist_id, title, priority, anchor_type, recurrence, sort_order)
VALUES (:'home_id', 'Take out recycling', 0, 0,
  '{"type":2,"interval":1,"daysOfWeek":[2,5],"dayOfMonth":null,"nthWeekday":null,"startDate":"2026-08-01","endDate":null,"exceptionDates":[]}', 3);

-- ============================================================================
-- Completions — mirror the mockup's "in progress" morning: some done today,
-- one done yesterday to show history isn't wiped.
-- ============================================================================
INSERT INTO checklist_completions (checklist_item_id, occurrence_date, completed_at, status)
VALUES
  (:'vitamins_id',    CURRENT_DATE, now(), 0),
  (:'bed_id',         CURRENT_DATE, now(), 0),
  (:'workout_item_id',CURRENT_DATE, now(), 0),
  (:'deploy_id',      CURRENT_DATE, now(), 0),
  (:'triage_id',      CURRENT_DATE, now(), 0),
  (:'plants_id',      CURRENT_DATE, now(), 0),
  (:'vitamins_id',    CURRENT_DATE - 1, now() - INTERVAL '1 day', 0),
  (:'deploy_id',      CURRENT_DATE - 1, now() - INTERVAL '1 day', 0);

-- ============================================================================
-- Future tasks + reminders
-- ============================================================================
INSERT INTO future_tasks (title, notes, due_date, due_time, category, priority)
VALUES ('Pay electricity bill', 'Before the due date to avoid a late fee.', CURRENT_DATE, '18:00', 'Personal', 2)
RETURNING id AS bill_id \gset

INSERT INTO reminders (future_task_id, offset_minutes, fire_at_utc, channels, status)
VALUES (:'bill_id', 30, (CURRENT_DATE + TIME '18:00') AT TIME ZONE 'Asia/Kolkata' - INTERVAL '30 minutes', 3, 0);

INSERT INTO future_tasks (title, notes, due_date, category, priority)
VALUES ('Call the dentist to reschedule', NULL, CURRENT_DATE, 'Health', 1)
RETURNING id AS dentist_id \gset

INSERT INTO reminders (future_task_id, offset_minutes, fire_at_utc, channels, status)
VALUES (:'dentist_id', 0, (CURRENT_DATE + TIME '09:00') AT TIME ZONE 'Asia/Kolkata', 1, 0);

INSERT INTO future_tasks (title, notes, due_date, due_time, category, priority)
VALUES ('Submit quarterly tax declaration', NULL, CURRENT_DATE + 1, '14:00', 'Finance', 2)
RETURNING id AS tax_id \gset

INSERT INTO reminders (future_task_id, offset_minutes, fire_at_utc, channels, status) VALUES
  (:'tax_id', 1440, (CURRENT_DATE + 1 + TIME '14:00') AT TIME ZONE 'Asia/Kolkata' - INTERVAL '1 day', 3, 0),
  (:'tax_id', 60,   (CURRENT_DATE + 1 + TIME '14:00') AT TIME ZONE 'Asia/Kolkata' - INTERVAL '1 hour', 3, 0);

INSERT INTO future_tasks (title, notes, due_date, due_time, category, priority)
VALUES ('Renew passport', 'Appointment slot booked at PSK.', CURRENT_DATE + 24, '11:00', 'Personal', 3)
RETURNING id AS passport_id \gset

INSERT INTO reminders (future_task_id, offset_minutes, fire_at_utc, channels, status) VALUES
  (:'passport_id', 10080, (CURRENT_DATE + 24 + TIME '11:00') AT TIME ZONE 'Asia/Kolkata' - INTERVAL '7 days', 3, 0),
  (:'passport_id', 1440,  (CURRENT_DATE + 24 + TIME '11:00') AT TIME ZONE 'Asia/Kolkata' - INTERVAL '1 day', 3, 0);

INSERT INTO future_tasks (title, due_date, category, priority)
VALUES ('Domain & hosting renewal', CURRENT_DATE + 44, 'Work', 1)
RETURNING id AS domain_id \gset

INSERT INTO reminders (future_task_id, offset_minutes, fire_at_utc, channels, status)
VALUES (:'domain_id', 20160, (CURRENT_DATE + 44) AT TIME ZONE 'Asia/Kolkata' - INTERVAL '14 days', 3, 0);

-- ============================================================================
-- Standalone Tasks (simple_tasks) — never touched by the scheduling engine.
-- ============================================================================
INSERT INTO simple_tasks (title, priority, status, sort_order, completed_at) VALUES
  ('Compare health insurance plans', 3, 0, 0, NULL),
  ('Research standing desks',        2, 0, 1, NULL),
  ('Sort out old photo backups',     1, 0, 2, NULL),
  ('Look into a weekend trek',       0, 0, 3, NULL),
  ('Read up on EF Core migrations',  2, 0, 4, NULL),
  ('Declutter the garage',           1, 0, 5, NULL),
  ('Try that new ramen place',       0, 0, 6, NULL),
  ('Cancel unused streaming subscription', 1, 1, 7, now() - INTERVAL '2 day'),
  ('Book eye test',                  1, 1, 8, now() - INTERVAL '5 day'),
  ('Update resume',                  1, 1, 9, now() - INTERVAL '9 day');

\echo 'Seed complete.'
