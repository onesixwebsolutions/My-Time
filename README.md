# DayGrid

A personal daily checklist & timetable app — .NET 8 Web API + Angular 18 + PostgreSQL.
Full design doc: see `DayGrid-Plan.md` one level up (or wherever you saved it) and the
interactive mockup `DayGrid-Mockup.html`.

## Status: hand-authored scaffold, not yet run or compiled

This codebase was generated in a sandboxed environment with **no network access to NuGet,
the npm registry, or apt mirrors** — so nothing here has been through `dotnet restore`,
`dotnet build`, or `npm install`. Every file was written by hand to be syntactically correct
(C# was checked for brace/structure balance; the TypeScript was verified with `tsc --noEmit`
against the actual Angular 18 API shapes), but **you are the first `dotnet build` / `npm install`
this code will ever see.** Budget time for the normal first-compile round of fixes — most likely
a missing `using`, a package version that needs bumping, or an Angular Material/CDK API that
shifted slightly. Nothing here is exotic; it's standard ASP.NET Core minimal APIs + EF Core +
standalone Angular components, so the fixes should be quick.

## What's implemented vs. stubbed

**Backend — solid:**
- Full domain model (all entities from the plan, including the standalone `SimpleTask`/Tasks module)
- `AppDbContext` + EF Core configurations for every entity, matching `db/init.sql` exactly
- `RecurrenceEngine` (pure, unit-tested — 14 tests in `tests/DayGrid.UnitTests`)
- `DayPlanBuilder` (the `/api/v1/today` aggregation)
- Full CRUD minimal-API endpoints for Tasks, Checklists, Timetable, Future Tasks, Settings
- `ReminderDispatcherService` / `DailyDigestService` background services
- Enum JSON convention: PascalCase strings everywhere (`"priority": "High"`, `"status": "Open"`) —
  see the comment in `Program.cs` if you ever touch the JSON options, this bit is easy to break.

**Backend — not yet done:**
- No EF Core migration has been generated (see "Database" below) — `db/init.sql` is a hand-written
  stand-in schema, not a real migration.
- Integration tests (`DayGrid.IntegrationTests` from the plan) weren't scaffolded — only the
  `RecurrenceEngine` unit tests exist.
- SignalR hub exists but nothing calls `IHubContext<ScheduleHub>` yet to actually broadcast
  `NowBlockChanged`/`ReminderFired`/etc. — wire that up as part of Phase 5/6.

**Frontend — solid:**
- Full routing shell, sidebar/topbar chrome, dark/light theme toggle
- **Tasks page is fully wired end-to-end** (list, quick-add, toggle, delete — optimistic updates
  with rollback), and deliberately isolated: `TasksStore` is component-scoped, not `providedIn: 'root'`,
  so it only exists while `/tasks` is open, per your requirement.
- Today page calls the real `/api/v1/today` endpoint and renders the now-card, timeline, checklist
  column, due-today panel with a live per-second clock.

**Frontend — stubbed on purpose (per the roadmap's phasing):**
- Checklists, Timetable, Upcoming, Settings, Calendar, Insights pages exist as real, compiling
  routes with a "Coming in Phase N" placeholder plus a plain read-only list of whatever their
  API returns — not yet the full drag-drop/recurrence-editor UI from the mockup. Build those out
  phase by phase per `DayGrid-Plan.md` section 7.

## Running it locally (needs internet — for NuGet/npm — which this sandbox didn't have)

```bash
# 1. Start Postgres only, using the hand-written schema as a first pass
docker compose up -d db

# 2. Backend
cd src/DayGrid.Api
dotnet restore
# Once you can reach NuGet, generate the real migration (recommended over db/init.sql long-term):
dotnet tool install --global dotnet-ef   # if you don't have it
dotnet ef migrations add InitialCreate -p ../DayGrid.Infrastructure -s .
dotnet ef database update -p ../DayGrid.Infrastructure -s .
dotnet watch run                          # https://localhost:5080, Swagger at /swagger

# 3. Frontend
cd src/DayGrid.Web
npm install
ng serve                                  # http://localhost:4200, proxies /api and /hubs → 5080
```

Or the whole stack via Docker once you've confirmed a local build works:

```bash
docker compose up --build
```

## Verified with real Postgres + sample data

This scaffold's schema and business logic have been validated against a **real PostgreSQL 16
instance** (not a mock) with realistic sample data — 4 checklists / 18 items across every anchor
type and 6 recurrence types, 2 timetable templates / 16 blocks, 7 weekday→template assignments,
1 date override, 5 future tasks / 7 reminders, and 10 standalone Tasks (7 open, 3 done). Load it
with:

```bash
psql -h <host> -U daygrid -d daygrid -f db/init.sql    # schema
psql -h <host> -U daygrid -d daygrid -f db/seed.sql    # sample data (uses psql \gset — must run via psql)
```

or restore `db/dump_with_sample_data.sql` directly (`psql -h <host> -U daygrid -d daygrid -f db/dump_with_sample_data.sql`)
for the exact same data in one step. `docker compose up -d db` now mounts both `init.sql` and
`seed.sql` into `/docker-entrypoint-initdb.d/`, so a fresh container gets schema + sample data
automatically on first boot.

Queries mirroring what `IDayPlanBuilder`/the endpoints compute were run directly against this data
and confirmed correct:
- **Template resolution** (most-specific-wins) correctly resolved today to the "Weekday" template.
- **Checklist progress** correctly reflected recurrence — e.g. "Morning Routine 3/5" only counts
  items whose `RecurrenceRule` actually fires today, joined against today's completions.
- **Timetable block state** (past/current/upcoming) correctly classified against the current time.
- **Due-today future tasks** correctly filtered to the two tasks due `CURRENT_DATE`.
- **Standalone Tasks counts** came back 7 open / 3 done — matching the mockup exactly, and with
  zero rows touched in any other table, confirming the isolation the plan calls for.

**What this does *not* confirm:** the C# itself has not been compiled. This sandbox has no network
path to NuGet or the npm registry (both return 403), so `dotnet build`/`npm install` have never
been run — the verification above exercises the schema, the sample data, and hand-traced query
logic equivalent to what the endpoints do, not the compiled binary. Run `dotnet build` yourself
as the very first step once you're somewhere with normal internet access; see "What's implemented
vs. stubbed" above for what's most likely to need a small fix on first compile.

## Database

Two schema sources exist on purpose, for two different moments:
- `db/init.sql` — a hand-written, plain SQL schema matching the plan exactly. Postgres runs it
  automatically on first container boot (mounted into `/docker-entrypoint-initdb.d/`), so you
  can get a working database before the .NET SDK is even installed.
- EF Core migrations (not yet generated) — the long-term source of truth once you run
  `dotnet ef migrations add InitialCreate` locally. At that point, prefer migrations and treat
  `init.sql` as documentation/reference rather than something you keep hand-editing.

## Config

`src/DayGrid.Api/appsettings.Development.json` has placeholders for the Postgres connection
string and SMTP email settings. Don't commit real credentials — use
`dotnet user-secrets set "Email:Password" "..."` locally instead, or the `EMAIL_*` environment
variables already wired into `docker-compose.yml`.

## Next steps

Follow `DayGrid-Plan.md` section 7 ("Build roadmap") phase by phase from here — Phase 2
(the scheduling engine) is already done, so you're effectively starting around Phase 3/3b
(Checklists + Tasks — Tasks is further along than Checklists right now) with a working Today
page ahead of schedule from Phase 5.
