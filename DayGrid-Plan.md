# DayGrid — Daily Checklist & Timetable
## Technical Blueprint & Build Plan

**Version** 1.0 · **Date** 19 Aug 2026
**Stack** .NET 8 Web API · Angular 18 · PostgreSQL 16 · EF Core 8

---

## 1. What we are building

A personal productivity app that answers one question the moment you open it:
**"What should I be doing right now, and what else does today expect of me?"**

Five pillars:

| # | Pillar | What it does |
|---|--------|--------------|
| 1 | **Today (Home)** | Live "now" card, today's timetable rail, today's checklist column |
| 2 | **Checklists** | Create/edit/delete checklists and items with recurrence and time anchoring |
| 3 | **Timetable** | Build reusable day templates out of hour/minute blocks; assign to weekdays or specific dates |
| 4 | **Upcoming** | One-off future tasks with reminders delivered in-app and by email |
| 5 | **Tasks** (new) | A private backlog — add / edit / remove entries, visible only inside this section, never surfaced on Today |

### Decisions locked in

| Concern | Decision | Why |
|---|---|---|
| Auth | **None** — single user | Removes an entire subsystem. A `UserId` column defaulting to a fixed GUID is still carried on every table so multi-user can be bolted on later without a migration rewrite. |
| Database | **PostgreSQL 16 + EF Core 8** | Free, first-class Docker story, excellent date/time and JSONB support (used for recurrence rules). |
| Notifications | **In-app + email** | SignalR pushes live to an open tab; a background scheduler sends email via MailKit for anything due when you're not looking. |
| Time handling | **`timestamptz` everywhere in the DB, `TimeOnly`/`DateOnly` for wall-clock concepts** | A 07:30 gym block means 07:30 local, always — it must not shift with DST. Absolute instants (reminder fire times, audit stamps) are stored as UTC. |
| Hosting | Docker Compose: `api`, `web` (nginx), `db` | Runs identically on your machine and any VPS. |

---

## 2. Architecture

```
┌──────────────────────────────────────────────────────────────┐
│  Angular 18 SPA  (standalone components, signals, Tailwind)  │
│  nginx :80  →  /api proxied to the API                       │
└───────────────┬──────────────────────────┬───────────────────┘
                │ REST (JSON)              │ WebSocket
                ▼                          ▼
┌──────────────────────────────────────────────────────────────┐
│  DayGrid.Api  (.NET 8, Minimal API + endpoint groups)        │
│  ├─ Endpoints        thin: bind → validate → call service    │
│  ├─ Hubs             ScheduleHub (SignalR)                   │
│  └─ Middleware       ProblemDetails exception handler         │
├──────────────────────────────────────────────────────────────┤
│  DayGrid.Application                                          │
│  ├─ Services         ChecklistService, TimetableService,      │
│  │                   TodayService, FutureTaskService          │
│  ├─ Scheduling       RecurrenceEngine, DayPlanBuilder,        │
│  │                   OccurrenceMaterialiser                   │
│  ├─ Notifications    INotificationChannel →                   │
│  │                   InAppChannel | EmailChannel              │
│  └─ Validation       FluentValidation validators              │
├──────────────────────────────────────────────────────────────┤
│  DayGrid.Domain      entities, enums, RecurrenceRule VO       │
├──────────────────────────────────────────────────────────────┤
│  DayGrid.Infrastructure                                       │
│  ├─ AppDbContext + configurations + migrations                │
│  ├─ Repositories (only where queries get gnarly)              │
│  ├─ MailKit email sender                                      │
│  └─ ReminderDispatcherService (IHostedService, 60s tick)      │
└───────────────────────────────┬──────────────────────────────┘
                                ▼
                        PostgreSQL 16
```

### Projects

```
DayGrid.sln
├── src/
│   ├── DayGrid.Domain/            (no dependencies)
│   ├── DayGrid.Application/       → Domain
│   ├── DayGrid.Infrastructure/    → Application, Domain
│   ├── DayGrid.Api/               → all of the above
│   └── DayGrid.Web/               Angular workspace
└── tests/
    ├── DayGrid.UnitTests/         RecurrenceEngine, DayPlanBuilder
    └── DayGrid.IntegrationTests/  WebApplicationFactory + Testcontainers
```

### NuGet / npm

**API** — `Npgsql.EntityFrameworkCore.PostgreSQL`, `FluentValidation.AspNetCore`, `Mapster`, `MailKit`, `Serilog.AspNetCore`, `Microsoft.AspNetCore.SignalR`, `Swashbuckle`, `Testcontainers.PostgreSql`, `Quartz` *(optional — the built-in `BackgroundService` is enough at this scale)*.

**Web** — `@angular/*` 18, `tailwindcss`, `@angular/cdk` (drag-drop + overlays), `@microsoft/signalr`, `date-fns`, `lucide-angular`, `ngx-sonner` (toasts).

---

## 3. The scheduling model — the heart of the app

Three separate concepts feed one screen. Keeping them separate is what makes the app flexible.

```
TIMETABLE  ─ "the shape of my day"     → 06:00–07:00 Workout, 09:00–12:30 Deep work
CHECKLIST  ─ "things that must happen" → Take vitamins, Review inbox, Water plants
FUTURE     ─ "a one-off on a date"     → 12 Sep: renew passport
                      ↓
              DayPlanBuilder(date)
                      ↓
     one merged, ordered view of a single day
```

**`RecurrenceRule`** is a value object stored as JSONB:

```jsonc
{
  "type": "Weekly",              // None | Daily | Weekly | MonthlyByDay | MonthlyByWeekday | EveryNDays | Custom
  "interval": 1,                 // every N days/weeks/months
  "daysOfWeek": [1,2,3,4,5],     // 0=Sun
  "dayOfMonth": null,
  "nthWeekday": null,            // e.g. { "nth": 2, "weekday": 2 } = 2nd Tuesday
  "startDate": "2026-08-01",
  "endDate": null,
  "exceptionDates": ["2026-12-25"]
}
```

`RecurrenceEngine.Occurs(rule, date)` is a pure function — no DB, no clock, fully unit-testable. Everything else in the scheduling layer builds on it.

**Materialisation strategy:** occurrences are computed on read, *not* pre-generated into rows. Only when you tick something does a `ChecklistCompletion` row get written for that (itemId, date). This keeps the DB tiny and lets you edit a recurring item without rewriting history.

---
## 4. Database schema

### 4.1 Entity relationship map

```
Checklist 1──* ChecklistItem 1──* ChecklistCompletion
                    │
                    └── RecurrenceRule (JSONB, embedded)

TimetableTemplate 1──* TimetableBlock
        │                     │
        │                     └── linked ChecklistId (nullable)
        └──* TimetableAssignment   (weekday OR specific date)

DayOverride  *──1 date   (skip day / swap template / holiday)

FutureTask 1──* Reminder ──* NotificationLog

AppSetting  (single row: timezone, email prefs, day start/end, theme)
```

### 4.2 Tables

#### `checklists`
Grouping container — "Morning Routine", "Work", "Health".

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `name` | varchar(120) NOT NULL | |
| `description` | text NULL | |
| `color` | varchar(9) | hex, drives the UI accent |
| `icon` | varchar(40) | lucide icon name |
| `sort_order` | int | manual drag ordering |
| `is_archived` | bool default false | soft delete — keeps history intact |
| `created_at` / `updated_at` | timestamptz | |

#### `checklist_items`
The actual to-dos. This is where recurrence and time anchoring live.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `checklist_id` | uuid FK → checklists | cascade delete |
| `title` | varchar(200) NOT NULL | |
| `notes` | text NULL | |
| `priority` | smallint | 0 Low · 1 Normal · 2 High · 3 Critical |
| `estimated_minutes` | int NULL | used to size the timeline card |
| `anchor_type` | smallint | 0 `Anytime` · 1 `FixedTime` · 2 `TimeWindow` · 3 `LinkedToBlock` |
| `anchor_time` | time NULL | for FixedTime |
| `window_start` / `window_end` | time NULL | for TimeWindow |
| `timetable_block_id` | uuid NULL FK | for LinkedToBlock — item rides along with a timetable block |
| `recurrence` | jsonb NOT NULL | `RecurrenceRule`; `{"type":"None"}` = one-off |
| `due_date` | date NULL | only meaningful when recurrence is None |
| `reminder_offset_minutes` | int NULL | fire N minutes before anchor_time |
| `is_active` | bool default true | |
| `sort_order` | int | |
| `created_at` / `updated_at` | timestamptz | |

*Indexes:* `(checklist_id, is_active)`, `(anchor_type, anchor_time)`, GIN on `recurrence`.

#### `checklist_completions`
One row per tick. Absence of a row = not done.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `checklist_item_id` | uuid FK | cascade |
| `occurrence_date` | date NOT NULL | the day it was owed |
| `completed_at` | timestamptz NOT NULL | when you actually ticked it |
| `status` | smallint | 0 Done · 1 Skipped · 2 Partial |
| `note` | text NULL | |

*Unique index:* `(checklist_item_id, occurrence_date)` — makes toggling idempotent and gives you streak stats for free.

#### `timetable_templates`
A named shape of a day: "Weekday", "Weekend", "Gym Day", "Travel".

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `name` | varchar(120) NOT NULL | |
| `description` | text NULL | |
| `is_default` | bool | fallback when no assignment matches |
| `day_start` | time default 06:00 | rail rendering bounds |
| `day_end` | time default 23:00 | |
| `slot_minutes` | smallint default 30 | grid granularity: 15/30/60 |
| `created_at` / `updated_at` | timestamptz | |

#### `timetable_blocks`
The hour/minute rows inside a template.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `template_id` | uuid FK → timetable_templates | cascade |
| `title` | varchar(160) NOT NULL | |
| `start_time` | time NOT NULL | |
| `end_time` | time NOT NULL | must be > start_time (CHECK constraint) |
| `category` | smallint | 0 Work · 1 Personal · 2 Health · 3 Learning · 4 Break · 5 Sleep · 6 Other |
| `color` | varchar(9) NULL | overrides category colour |
| `location` | varchar(120) NULL | |
| `checklist_id` | uuid NULL FK | surfaces that checklist's items inside this block |
| `allow_overlap` | bool default false | validation escape hatch |
| `notify_at_start` | bool default false | |
| `sort_order` | int | tie-break for same start_time |

*Index:* `(template_id, start_time)`.
*Overlap rule:* enforced in the service layer, not the DB — an interval check against sibling blocks unless `allow_overlap`.

#### `timetable_assignments`
Which template applies when. Resolution is **most-specific-wins**.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `template_id` | uuid FK | |
| `scope` | smallint | 0 `Weekday` · 1 `SpecificDate` · 2 `DateRange` |
| `day_of_week` | smallint NULL | 0=Sun … 6=Sat |
| `date_from` / `date_to` | date NULL | |
| `priority` | int default 0 | manual tie-break |

Resolution order for a given date: `SpecificDate` → `DateRange` → `Weekday` → template with `is_default`.

#### `day_overrides`
Escape hatch for real life.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `date` | date UNIQUE NOT NULL | |
| `mode` | smallint | 0 `UseTemplate` · 1 `RestDay` (hide everything) · 2 `CustomOnly` |
| `template_id` | uuid NULL FK | for UseTemplate |
| `note` | varchar(200) NULL | shown as a banner on Home |

#### `future_tasks`
One-off things on a future date that must produce a notification.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `title` | varchar(200) NOT NULL | |
| `notes` | text NULL | |
| `due_date` | date NOT NULL | |
| `due_time` | time NULL | null = all-day |
| `category` | varchar(60) NULL | |
| `priority` | smallint | |
| `status` | smallint | 0 Pending · 1 Done · 2 Cancelled · 3 Deferred |
| `completed_at` | timestamptz NULL | |
| `promote_to_checklist_id` | uuid NULL FK | on the due date it also appears in that checklist column |
| `created_at` / `updated_at` | timestamptz | |

*Index:* `(status, due_date)`.

#### `reminders`
Explicit fire schedule. A task can have several ("1 week before" + "on the day").

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `future_task_id` | uuid NULL FK | cascade |
| `checklist_item_id` | uuid NULL FK | cascade — for daily-item reminders |
| `offset_minutes` | int NOT NULL | minutes before the due moment (10080 = 1 week) |
| `fire_at_utc` | timestamptz NOT NULL | computed on save; recomputed if the task moves |
| `channels` | smallint | bit flags: 1 InApp · 2 Email |
| `status` | smallint | 0 Scheduled · 1 Sent · 2 Failed · 3 Cancelled |
| `sent_at_utc` | timestamptz NULL | |
| `attempt_count` | int default 0 | |

*Index:* `(status, fire_at_utc)` — the dispatcher's only hot query.
*CHECK:* exactly one of `future_task_id` / `checklist_item_id` is non-null.

#### `notification_log`
What was actually delivered — powers the in-app bell and prevents duplicates.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `reminder_id` | uuid NULL FK | |
| `title` / `body` | varchar / text | |
| `channel` | smallint | |
| `created_at_utc` | timestamptz | |
| `read_at_utc` | timestamptz NULL | |
| `error` | text NULL | |

#### `app_settings` — single row

| Column | Type | Default |
|---|---|---|
| `id` | int PK CHECK (id = 1) | 1 |
| `time_zone` | varchar(60) | `Asia/Kolkata` |
| `week_starts_on` | smallint | 1 (Mon) |
| `day_start` / `day_end` | time | 06:00 / 23:00 |
| `default_slot_minutes` | smallint | 30 |
| `email_enabled` | bool | true |
| `email_to` | varchar(200) | |
| `daily_digest_time` | time NULL | 07:00 — "here's your day" email |
| `theme` | varchar(20) | `system` |

### 4.2b `simple_tasks` — the standalone Tasks section

This is deliberately **not** the same thing as a `checklist_item` or a `future_task`. It has no recurrence,
no time anchor, no reminder, and is never pulled into the `/today` aggregate. It exists for the case you
described: a private scratch list you only want to see — and only want to be able to add to, edit, or
remove from — when you've actually opened the Tasks section. Nothing about it leaks onto the home page.

| Column | Type | Notes |
|---|---|---|
| `id` | uuid PK | |
| `title` | varchar(200) NOT NULL | |
| `notes` | text NULL | |
| `priority` | smallint | 0 Low · 1 Normal · 2 High · 3 Critical |
| `status` | smallint | 0 Open · 1 Done |
| `sort_order` | int | manual drag ordering within the section |
| `completed_at` | timestamptz NULL | |
| `created_at` / `updated_at` | timestamptz | |

*Index:* `(status, sort_order)`.

**Deliberate scope boundary:** `simple_tasks` rows are never read by `DayPlanBuilder`, never appear in
`TodayDto`, and have no `reminders` row. If a task on this list turns out to need a date and a reminder,
the move is to create a `future_tasks` entry (Upcoming) or a `checklist_items` entry (Checklists) —
`simple_tasks` stays the one place that's just "things I'm keeping track of, nothing more."

### 4.3 Seed data (dev)

Two templates (**Weekday**, **Weekend**) with realistic blocks, three checklists (**Morning Routine**, **Work**, **Evening Wind-down**) with ~12 items across all four anchor types and four recurrence types, and three future tasks — enough to exercise every screen on first run.

---
## 5. API surface

Minimal API with endpoint groups, `/api/v1` prefix, `ProblemDetails` on every error, Swagger in dev.

### 5.1 Today — the money endpoint

```http
GET /api/v1/today?date=2026-08-19
```

One call renders the entire home page. Returns:

```jsonc
{
  "date": "2026-08-19",
  "dayOfWeek": "Wednesday",
  "displayDate": "Wednesday, 19 August 2026",
  "override": null,                       // or { mode:"RestDay", note:"Sick leave" }
  "template": { "id":"...", "name":"Weekday", "dayStart":"06:00", "dayEnd":"23:00", "slotMinutes":30 },

  "nowBlock": {                           // what you should be doing THIS minute
    "blockId": "...", "title": "Deep Work — Project Alpha",
    "startTime": "09:00", "endTime": "12:30",
    "category": "Work", "color": "#3b82f6",
    "elapsedMinutes": 47, "remainingMinutes": 163, "progressPercent": 22
  },
  "nextBlock": { "title":"Lunch", "startTime":"12:30", "startsInMinutes":163 },

  "blocks": [                             // the full timetable rail
    { "id":"...", "title":"Workout", "startTime":"06:00", "endTime":"07:00",
      "category":"Health", "color":"#22c55e", "location":"Home gym",
      "state":"past",                     // past | current | upcoming
      "linkedItems":[ { "itemId":"...", "title":"Stretch 10 min", "isCompleted":true } ] }
  ],

  "checklists": [                         // the checklist column
    { "checklistId":"...", "name":"Morning Routine", "color":"#f59e0b", "icon":"sunrise",
      "completedCount":3, "totalCount":5,
      "items":[
        { "itemId":"...", "title":"Take vitamins", "anchorType":"FixedTime",
          "anchorTime":"07:15", "priority":"Normal", "estimatedMinutes":2,
          "isCompleted":true, "completedAt":"2026-08-19T07:12:00Z",
          "isOverdue":false, "recurrenceLabel":"Every day" }
      ] }
  ],

  "dueToday":  [ { "id":"...", "title":"Renew passport", "dueTime":"11:00", "priority":"High" } ],
  "overdue":   [ ],
  "summary": { "totalItems":14, "completedItems":6, "completionPercent":43,
               "blocksTotal":8, "blocksDone":3, "minutesScheduled":510 }
}
```

Supporting: `GET /api/v1/today/now` (tiny poll fallback for the current-block card), `GET /api/v1/days/{date}/summary`, `GET /api/v1/days/range?from=&to=` (week/month views).

### 5.2 Checklists

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/checklists?includeArchived=false` | list with item counts |
| GET | `/api/v1/checklists/{id}` | one checklist + its items |
| POST | `/api/v1/checklists` | create |
| PUT | `/api/v1/checklists/{id}` | update |
| PATCH | `/api/v1/checklists/{id}/archive` | archive / restore |
| DELETE | `/api/v1/checklists/{id}` | hard delete (cascades) |
| PUT | `/api/v1/checklists/reorder` | `[{id, sortOrder}]` |

### 5.3 Checklist items

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/checklists/{id}/items` | |
| POST | `/api/v1/checklists/{id}/items` | create |
| PUT | `/api/v1/items/{itemId}` | update (incl. recurrence + anchor) |
| DELETE | `/api/v1/items/{itemId}` | |
| PATCH | `/api/v1/items/{itemId}/active` | pause without deleting |
| PUT | `/api/v1/checklists/{id}/items/reorder` | |
| POST | `/api/v1/items/{itemId}/complete` | body `{ date, status, note }` → upsert completion |
| DELETE | `/api/v1/items/{itemId}/complete?date=` | untick |
| POST | `/api/v1/items/{itemId}/skip` | body `{ date, reason }` |
| GET | `/api/v1/items/{itemId}/history?from=&to=` | completion history + current streak |
| POST | `/api/v1/items/preview-occurrences` | body = a `RecurrenceRule` + range → dates it would fire. **Powers the live "this fires on…" preview in the recurrence editor.** |

**Create item request**

```jsonc
{
  "title": "Review PR queue",
  "notes": "Anything older than 24h gets a comment",
  "priority": 2,
  "estimatedMinutes": 20,
  "anchorType": "FixedTime",          // Anytime | FixedTime | TimeWindow | LinkedToBlock
  "anchorTime": "16:30",
  "windowStart": null, "windowEnd": null,
  "timetableBlockId": null,
  "recurrence": { "type":"Weekly", "interval":1, "daysOfWeek":[1,2,3,4,5],
                  "startDate":"2026-08-01", "endDate":null, "exceptionDates":[] },
  "reminderOffsetMinutes": 10
}
```

### 5.4 Timetable

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/timetable/templates` | |
| GET | `/api/v1/timetable/templates/{id}` | template + blocks |
| POST/PUT/DELETE | `/api/v1/timetable/templates[/{id}]` | CRUD |
| POST | `/api/v1/timetable/templates/{id}/duplicate` | "Weekday → Weekday (Summer)" |
| PATCH | `/api/v1/timetable/templates/{id}/default` | set as fallback |
| GET/POST | `/api/v1/timetable/templates/{id}/blocks` | |
| PUT/DELETE | `/api/v1/timetable/blocks/{blockId}` | |
| PUT | `/api/v1/timetable/blocks/{blockId}/move` | `{startTime, endTime}` — drag-resize on the grid |
| POST | `/api/v1/timetable/blocks/validate` | overlap check before save; returns conflicting blocks |
| GET/POST/DELETE | `/api/v1/timetable/assignments[/{id}]` | which template on which day |
| GET | `/api/v1/timetable/resolve?date=` | which template wins for a date, and why |
| GET/PUT/DELETE | `/api/v1/day-overrides[/{date}]` | rest days, swaps, notes |

### 5.5 Future tasks & reminders

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/future-tasks?status=&from=&to=&q=` | filter + search |
| GET | `/api/v1/future-tasks/upcoming?days=30` | grouped Today / Tomorrow / This week / Later |
| POST/PUT/DELETE | `/api/v1/future-tasks[/{id}]` | CRUD (reminders sent inline) |
| PATCH | `/api/v1/future-tasks/{id}/status` | done / cancel |
| PATCH | `/api/v1/future-tasks/{id}/defer` | `{ newDueDate }` — reschedules reminders |
| GET/POST/DELETE | `/api/v1/future-tasks/{id}/reminders[/{rid}]` | |
| GET | `/api/v1/notifications?unreadOnly=true&take=50` | bell dropdown |
| PATCH | `/api/v1/notifications/{id}/read` · `/read-all` | |
| POST | `/api/v1/notifications/test` | fires a test in-app + email — verifies SMTP config |

**Create future task**

```jsonc
{
  "title": "Renew passport",
  "notes": "Appointment slot booked at PSK",
  "dueDate": "2026-09-12",
  "dueTime": "11:00",
  "category": "Personal",
  "priority": 3,
  "promoteToChecklistId": null,
  "reminders": [
    { "offsetMinutes": 10080, "channels": ["InApp","Email"] },   // 1 week before
    { "offsetMinutes": 1440,  "channels": ["InApp","Email"] },   // 1 day before
    { "offsetMinutes": 30,    "channels": ["InApp"] }
  ]
}
```

### 5.5b Tasks (standalone)

Deliberately isolated from every other module — no `/today` involvement, no reminders, no recurrence.

| Method | Route | Purpose |
|---|---|---|
| GET | `/api/v1/tasks?status=&q=&sort=` | list, optionally filtered/searched |
| POST | `/api/v1/tasks` | create — `{ title, notes, priority }` |
| PUT | `/api/v1/tasks/{id}` | update |
| PATCH | `/api/v1/tasks/{id}/status` | `{ status: "Done" \| "Open" }` |
| DELETE | `/api/v1/tasks/{id}` | remove |
| PUT | `/api/v1/tasks/reorder` | `[{id, sortOrder}]` |

`TasksController`/`TasksService` has no dependency on `DayPlanBuilder`, `RecurrenceEngine`, or the
reminder dispatcher — it is the simplest module in the app on purpose, and stays that way.

### 5.6 Settings & utility

`GET/PUT /api/v1/settings` · `GET /api/v1/stats/streaks` · `GET /api/v1/stats/completion?from=&to=` · `GET /api/v1/export` (full JSON backup) · `POST /api/v1/import` · `GET /health`.

### 5.7 SignalR — `/hubs/schedule`

| Event | Payload | Fires when |
|---|---|---|
| `NowBlockChanged` | current + next block | a timetable block boundary is crossed |
| `ReminderFired` | notification DTO | dispatcher sends a reminder |
| `ItemCompleted` | `{itemId, date, status}` | keeps other open tabs in sync |
| `PlanInvalidated` | `{date}` | template/assignment edited → client refetches |

### 5.8 Reminder dispatcher

`ReminderDispatcherService : BackgroundService`, 60-second tick:

1. `SELECT * FROM reminders WHERE status = Scheduled AND fire_at_utc <= now() ORDER BY fire_at_utc LIMIT 100` — with `FOR UPDATE SKIP LOCKED` so a second instance can never double-send.
2. For each: build the payload, fan out to the enabled channels.
3. `InAppChannel` → write `notification_log` + `ScheduleHub.ReminderFired`.
4. `EmailChannel` → MailKit SMTP, HTML template, retry ×3 with exponential backoff; on final failure mark `Failed` and log the error (the in-app copy still lands, so nothing is silently lost).
5. Mark `Sent`, stamp `sent_at_utc`.

A second hosted service, `DailyDigestService`, runs at `app_settings.daily_digest_time` and emails the day's plan.

**Note on block-start alerts.** `timetable_blocks.notify_at_start` does *not* create `reminders` rows — a template block has no single date, so there is nothing to compute a `fire_at_utc` from. Instead the same 60-second tick evaluates today's resolved plan, and when a block with `notify_at_start` begins it emits `NowBlockChanged` plus a `notification_log` entry directly. Only `future_tasks` and `checklist_items` — both of which resolve to a concrete date — go through the `reminders` table.

Reminder `fire_at_utc` is recomputed inside the same transaction whenever a task's due date/time changes, so a deferred task never fires on its old schedule.

---
## 6. Angular frontend

Angular 18 · standalone components · signals for state · Tailwind CSS · Angular CDK for drag-drop.

### 6.1 Routes

```
/                    → redirect to /today
/today               HomePage            "What now?"
/today/:date         HomePage            any date, same component
/checklists          ChecklistsPage      manage checklists + items
/checklists/:id      ChecklistDetailPage
/timetable           TimetablePage       template builder (grid editor)
/timetable/schedule  AssignmentsPage     which template on which day
/upcoming            UpcomingPage        future tasks + reminders
/tasks               TasksPage           standalone backlog — own CRUD, not shown elsewhere
/calendar            CalendarPage        month heat-map (Phase 5)
/insights            InsightsPage        streaks & completion charts (Phase 5)
/settings            SettingsPage
```

### 6.2 Folder structure

```
src/app/
├── core/
│   ├── api/            generated-ish typed clients: today.api.ts, checklists.api.ts,
│   │                   timetable.api.ts, future-tasks.api.ts, settings.api.ts
│   ├── realtime/       signalr.service.ts  (auto-reconnect, typed events)
│   ├── state/          today.store.ts, checklists.store.ts, timetable.store.ts,
│   │                   notifications.store.ts   ← signal stores, no NgRx needed
│   ├── time/           clock.service.ts (1s tick signal), tz.util.ts
│   └── interceptors/   error.interceptor.ts, loading.interceptor.ts
├── shared/
│   ├── ui/             Button, Card, Modal, Drawer, Chip, Toggle, Select,
│   │                   TimePicker, ColorPicker, IconPicker, EmptyState, Skeleton
│   ├── pipes/          timeAgo, duration, recurrenceLabel, dayName
│   └── directives/     autofocus, clickOutside, longPress (mobile)
├── features/
│   ├── today/          home-page, now-card, timeline-rail, timeline-block,
│   │                   checklist-column, checklist-group, checklist-row,
│   │                   day-progress-ring, day-nav, due-today-panel
│   ├── checklists/     checklists-page, checklist-form, item-form,
│   │                   recurrence-editor, anchor-editor, occurrence-preview
│   ├── timetable/      timetable-page, template-list, block-grid, block-editor,
│   │                   conflict-banner, assignments-page, override-editor
│   ├── upcoming/       upcoming-page, task-form, reminder-editor, task-group
│   ├── tasks/          tasks-page, task-row, task-quick-add   (own store, own API client —
│   │                   deliberately not imported by today/ or any other feature)
│   ├── notifications/  bell, notification-panel, toast-host
│   └── settings/       settings-page, email-settings, appearance-settings
└── layout/             app-shell, sidebar, topbar, mobile-nav
```

### 6.3 Home page anatomy

```
┌─────────────────────────────────────────────────────────────────────┐
│  ☰  DayGrid                              🔍   🔔 3   ⚙︎   ☾/☀︎        │  topbar
├─────────────────────────────────────────────────────────────────────┤
│  ◀  Wednesday, 19 August 2026  ▶      [Today]     Weekday template  │  day-nav
├─────────────────────────────────────────────────────────────────────┤
│ ╔═══════════════════════════════════════════════════════════════╗   │
│ ║  ● NOW · 09:47                                                ║   │  now-card
│ ║  Deep Work — Project Alpha                    ⏱ 2h 43m left   ║   │  (accent
│ ║  09:00 ─────────────●──────────────────────────────── 12:30   ║   │   gradient,
│ ║  ▸ Next at 12:30 · Lunch                                      ║   │   pulsing dot)
│ ╚═══════════════════════════════════════════════════════════════╝   │
├───────────────────────────────┬─────────────────────────────────────┤
│  TIMETABLE                    │  CHECKLIST                     6/14 │
│  ┌──┬──────────────────────┐  │  ┌───────────────────────────────┐  │
│  │06│ ▓ Workout      06–07 │  │  │ ◐ Morning Routine        3/5  │  │
│  │07│ ▓ Breakfast    07–08 │  │  │   ☑ Take vitamins      07:15  │  │
│  │08│ ░ Commute      08–09 │  │  │   ☑ Make bed                  │  │
│  │09│ █ Deep Work  ← NOW   │  │  │   ☐ Journal            07:40  │  │
│  │10│ █   (spans)          │  │  ├───────────────────────────────┤  │
│  │11│ █                    │  │  │ ◐ Work                   2/6  │  │
│  │12│ ▓ Lunch        12:30 │  │  │   ☐ Review PR queue    16:30  │  │
│  │13│ ▓ Meetings     13–15 │  │  │   ⚠ Standup notes      09:30  │  │  overdue
│  └──┴──────────────────────┘  │  └───────────────────────────────┘  │
│  hour gutter + proportional   │  ┌───────────────────────────────┐  │
│  blocks, red "now" line       │  │ 📅 DUE TODAY                  │  │
│  auto-scrolls to current time │  │   Renew passport      11:00   │  │
└───────────────────────────────┴─────────────────────────────────────┘
```

Desktop: two columns (timeline 58% / checklist 42%). Tablet: stacked. Mobile: segmented tabs `Now | Timeline | Checklist` with a bottom nav bar.

### 6.4 Key interactions

| Interaction | Behaviour |
|---|---|
| **Live clock** | `ClockService` emits a signal every second. The now-card progress bar and the red "now" line move without any network call. |
| **Tick an item** | Optimistic: the row strikes through and the ring animates instantly, then POSTs. On failure it reverts and toasts. |
| **Recurrence editor** | Segmented control (Never / Daily / Weekly / Monthly / Custom) → contextual controls → a live "Fires on: Mon 24, Tue 25, Wed 26…" preview fed by `POST /items/preview-occurrences`. This is what makes recurrence feel simple instead of scary. |
| **Timetable grid editor** | A CSS-grid day column, 15-min rows. Drag on empty space to create a block, drag a block to move it, drag its edge to resize. Overlaps highlight in amber and a conflict banner offers *Auto-fix / Allow overlap*. |
| **Assignments** | A 7-chip weekday row; click a day, pick a template. Below it a mini-calendar for date-specific overrides. |
| **Bell** | SignalR pushes `ReminderFired` → toast + badge increment. The panel groups by Today / Earlier. |
| **Keyboard** | `T` today · `←/→` prev/next day · `N` new item · `C` new checklist · `/` search · `Space` toggle focused item. |

### 6.5 UI design system

**Palette** (Tailwind config, works in both themes)

| Token | Light | Dark | Use |
|---|---|---|---|
| `surface` | `#ffffff` | `#0f1117` | page |
| `surface-raised` | `#f8fafc` | `#171a21` | cards |
| `border` | `#e2e8f0` | `#262b36` | hairlines |
| `text` | `#0f172a` | `#e6e8ee` | primary text |
| `text-muted` | `#64748b` | `#8b93a7` | secondary |
| `accent` | `#4f46e5` | `#6366f1` | now-card, primary buttons |
| `success` | `#16a34a` | `#22c55e` | completed |
| `warning` | `#d97706` | `#f59e0b` | due soon |
| `danger` | `#dc2626` | `#ef4444` | overdue |

Block categories: Work `#3b82f6` · Personal `#a855f7` · Health `#22c55e` · Learning `#f59e0b` · Break `#14b8a6` · Sleep `#64748b` · Other `#94a3b8`. Every colour pair meets WCAG AA (≥4.5:1) against its surface.

**Craft details that carry the "great UI" ask**

- Type scale 12/14/16/20/28/36, Inter (or system stack), tabular numerals for all clock times so digits don't jitter.
- 8px spacing grid, 12px card radius, one soft shadow (`0 1px 3px rgba(0,0,0,.08)`) — no heavy chrome.
- Past blocks drop to 55% opacity; the current block gets a 2px accent ring and subtle glow; upcoming blocks are full-strength.
- Completion ring animates with a stroke-dashoffset transition, not a jump.
- Every list has a designed empty state (illustration + one-line copy + primary action), never a blank box.
- Skeleton loaders shaped like the real content, not spinners.
- `prefers-reduced-motion` respected: transitions collapse to instant.
- Full keyboard focus rings, ARIA roles on the timeline (`role="list"`), live region announcing the current block.

---
## 7. Build roadmap

Eight phases. Each ends with something you can actually run and look at.

### Phase 0 — Foundation (½ day)
- `dotnet new sln`, four class libraries + API, `ng new` inside `src/DayGrid.Web`.
- Docker Compose: `postgres:16`, api, web. `.env` for connection string + SMTP.
- Serilog, ProblemDetails handler, CORS, health check, Swagger.
- **Done when:** `docker compose up` serves a Swagger page and an Angular splash.

### Phase 1 — Domain & data (1 day)
- All entities, enums, `RecurrenceRule` value object.
- `AppDbContext` + `IEntityTypeConfiguration` per entity, JSONB mapping, CHECK constraints, indexes.
- Initial migration + seed data.
- **Done when:** `dotnet ef database update` creates the schema and seeds a realistic day.

### Phase 2 — Scheduling engine (1–2 days) ⭐ *do this before any UI*
- `RecurrenceEngine.Occurs(rule, date)` and `.Expand(rule, from, to)`.
- `TemplateResolver` — most-specific-wins for a date, honouring `day_overrides`.
- `DayPlanBuilder` — merges blocks + checklist occurrences + completions + due future tasks into the `TodayDto`.
- Unit tests: every recurrence type, month-end edges (31st in February), DST boundaries, exception dates, endDate expiry, overlapping blocks.
- **Done when:** `GET /api/v1/today` returns a correct plan for any date you throw at it. This is the piece most likely to bite later, so it gets tests first.

### Phase 3 — Checklist module (1–2 days)
- Full CRUD + completion endpoints + `preview-occurrences`.
- Angular: Checklists page, checklist form, item form, **recurrence editor with live preview**, anchor editor.
- **Done when:** you can build a real recurring checklist end to end.

### Phase 3b — Tasks module (½ day) — do this alongside Phase 3
- `simple_tasks` table + `TasksController`/`TasksService`, no dependency on the scheduling engine.
- Angular: standalone Tasks page — list, quick-add, inline edit, delete, mark done, drag reorder.
- Deliberately **not** wired into `today.store.ts` or `DayPlanBuilder` — it renders only when `/tasks` is
  the active route and fetches/mutates only while that page is open.
- **Done when:** you can add, edit and remove backlog items from the Tasks section, and confirm none of it
  appears on the Today page.

### Phase 4 — Timetable module (2 days)
- Template/block/assignment CRUD, overlap validation, duplicate, resolve.
- Angular: grid editor with CDK drag-drop create/move/resize, conflict banner, assignments page, day overrides.
- **Done when:** you can draw your actual weekly schedule and see it resolve per date.

### Phase 5 — Home page (2 days) 🎯 *the payoff*
- Wire `/today` into the layout.
- Now-card with live clock, timeline rail with proportional blocks + red now-line + auto-scroll, checklist column with optimistic ticking, progress ring, day navigation, due-today panel.
- SignalR client: `NowBlockChanged`, `ItemCompleted`, `PlanInvalidated`.
- Responsive breakpoints, empty states, skeletons.
- **Done when:** you open the app in the morning and it tells you what to do.

### Phase 6 — Future tasks & notifications (1–2 days)
- Future task CRUD, reminder scheduling, defer/promote.
- `ReminderDispatcherService` with `SKIP LOCKED`, `InAppChannel`, `EmailChannel` (MailKit + HTML template + retry), `DailyDigestService`.
- Angular: Upcoming page grouped by horizon, reminder editor with presets (10 min / 1 h / 1 day / 1 week before), bell + notification panel + toasts, email settings with a **Send test email** button.
- **Done when:** a reminder set for two minutes out arrives both in the tab and in your inbox.

### Phase 7 — Polish & extras (1–2 days)
- Calendar month heat-map, streaks and completion charts, global search (`/`), keyboard shortcuts, dark mode toggle, export/import JSON backup.
- Accessibility pass, Lighthouse pass, integration tests with Testcontainers, Playwright smoke tests on the three core flows.

**Realistic total: 10–14 focused days.** Phases 0–5 (≈8 days) already give you a genuinely useful app; 6 and 7 are the difference between useful and polished.

---

## 8. Things that will bite, and the answers

| Risk | Mitigation |
|---|---|
| **DST / timezone drift** — a 07:00 block silently moving | Store wall-clock concepts as `time`/`date`, never as UTC instants. Convert to UTC only when computing `fire_at_utc`, using the IANA zone from settings. Unit-test across a DST boundary. |
| **Recurrence edge cases** — "31st of every month" in February | `RecurrenceEngine` is pure and tested first, before any UI depends on it. Explicit policy: clamp to the last valid day of the month. |
| **Editing a recurring item retroactively rewrites history** | Completions are stored per `(itemId, date)` and never regenerated. Editing an item changes the future only; past ticks stand. |
| **Duplicate reminder emails** | Single dispatcher + `FOR UPDATE SKIP LOCKED` + status transition inside the transaction. A reminder can only leave `Scheduled` once. |
| **Overlapping timetable blocks** | Interval validation in the service layer with an explicit `allowOverlap` opt-out, plus a UI conflict banner. Not a DB constraint — real days do sometimes overlap. |
| **`/today` getting slow** | It's a handful of indexed reads plus in-memory expansion. Cache the resolved template per date in `IMemoryCache`, invalidated on any timetable write. |
| **Scope creep** | Explicitly *not* in v1: multi-user, mobile app, calendar sync, subtasks, attachments, tags, time tracking. All are additive later. |

---

## 9. Local dev

```bash
# 1. Infrastructure
docker compose up -d db

# 2. API
cd src/DayGrid.Api
dotnet ef database update
dotnet watch run                # https://localhost:7080, Swagger at /swagger

# 3. Web
cd src/DayGrid.Web
npm install
ng serve                        # http://localhost:4200, proxies /api → 7080
```

`appsettings.Development.json`

```jsonc
{
  "ConnectionStrings": { "Default": "Host=localhost;Database=daygrid;Username=daygrid;Password=dev" },
  "Email": { "Host":"smtp.gmail.com", "Port":587, "UseStartTls":true,
             "User":"onesixwebsolutions@gmail.com", "Password":"<app-password>",
             "FromName":"DayGrid" },
  "App": { "TimeZone":"Asia/Kolkata" }
}
```

> Gmail needs an **App Password** (2FA on → Google Account → App passwords), not your normal password. Alternatives if you'd rather not use SMTP directly: Brevo or Resend free tiers, both drop-in.

---

## 10. Ready-to-use next step

When you want to start building, the first prompt to give is:

> *"Implement Phase 0 and Phase 1 of the DayGrid plan: solution scaffold, Docker Compose, all domain entities, EF Core configurations, the initial migration and the seed data."*

Then walk phase by phase. Phase 2 before any UI — the scheduling engine is the spine, and everything else hangs off it.
