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
- `AppDbContext` + EF Core configurations for every entity, matching `db/migrations/*.sql` exactly
  (enforced by the schema-parity integration tests)
- Accounts (ASP.NET Core Identity, cookie auth, email confirmation, admin role) and per-user data
  isolation — see "Accounts, security & multi-tenancy" below
- `RecurrenceEngine` (pure, unit-tested — 14 tests in `tests/DayGrid.UnitTests`)
- `DayPlanBuilder` (the `/api/v1/today` aggregation)
- Full CRUD minimal-API endpoints for Tasks, Checklists, Timetable, Future Tasks, Settings
- `ReminderDispatcherService` / `DailyDigestService` background services
- Enum JSON convention: PascalCase strings everywhere (`"priority": "High"`, `"status": "Open"`) —
  see the comment in `Program.cs` if you ever touch the JSON options, this bit is easy to break.

**Backend — not yet done:**
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
# 1. Start Postgres only (empty database; the API creates/upgrades the schema itself)
docker compose up -d db

# 2. Backend — Development runs the schema migrations at startup (Database:InitializeSchema=true)
cd src/DayGrid.Api
dotnet watch run                          # Swagger at /swagger (Development only)

# 3. Create your account: register in the web app. In Development, emails are written as .eml
#    files to src/DayGrid.Api/bin/<config>/net8.0/mail-pickup/ (Email:Mode=Pickup) — open the
#    newest one and follow the confirmation link. The FIRST account becomes Admin and takes
#    ownership of all data that existed before accounts were introduced.

# 4. Frontend
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
# after the API has created the schema once (or: psql -f db/migrations/0001_initial.sql -f ...0002...)
psql -h <host> -U daygrid -d daygrid -f db/seed.sql    # sample data (uses psql \gset — must run via psql)
```

Load it **before registering the first account**: seeded rows have no owner (`user_id` NULL) and
the first account to register claims them. `db/dump_with_sample_data.sql` is a pg_dump of the
*pre-accounts* schema (it also contains a `CREATE EXTENSION pgcrypto` that Azure rejects); restore
it into an empty database and start the API — the migrator baselines it at 0001 and upgrades it.
docker-compose no longer mounts any SQL into `/docker-entrypoint-initdb.d/` (the API owns the schema).

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

The schema is a series of hand-written, versioned SQL migrations in `db/migrations/NNNN_name.sql`
(embedded into `DayGrid.Infrastructure` and applied by `SchemaMigrator`):

| Migration | What |
|---|---|
| `0001_initial.sql` | the original schema (formerly `db/init.sql`) |
| `0002_auth_multitenancy.sql` | Identity tables (`users`, `roles`, `user_roles`, `user_claims`, `user_logins`, `user_tokens`, `role_claims`), `data_protection_keys`, `user_id` (FK → users, ON DELETE CASCADE) on every data table, per-user uniques (`day_overrides(user_id, date)`, `app_settings(user_id)`), roles User/Admin |

- Applied versions are recorded in `schema_migrations(version, applied_at)`; each migration runs in
  its own transaction; a session advisory lock serialises concurrent app starts.
- Runs at startup when `Database:InitializeSchema=true` (External mode — Azure, docker-compose and
  Development set it) and always in Embedded mode.
- A database created from the old `db/init.sql` (tables present, no `schema_migrations`) is
  detected, baselined at 0001 and upgraded in place; existing rows are kept with `user_id` NULL
  until the first account registers and claims them.
- No `CREATE EXTENSION` anywhere (Azure Database for PostgreSQL Flexible Server compatible).
- Never edit an applied migration — add `0003_...sql`. EF Core migrations are not used; the EF
  model is kept in step by `tests/DayGrid.IntegrationTests/Schema/SchemaParityTests.cs`.

## Accounts, security & multi-tenancy

- Email + password (ASP.NET Core Identity). Open registration with mandatory email confirmation;
  password ≥ 10 characters (max 128, no composition rules); 5 failed sign-ins lock the account for
  15 minutes; auth endpoints are rate limited per IP (10/min).
- Session = HttpOnly `daygrid.auth` cookie (SameSite=Strict, Secure outside Development, 14 days
  sliding with "remember me"). CSRF: readable `XSRF-TOKEN` cookie + `X-XSRF-TOKEN` header on
  every unsafe `/api` request. Unauthenticated API calls get 401 (never a redirect).
- Every data row belongs to a user: EF global query filters + insert stamping + a save-time
  check that rejects foreign keys pointing at another user's rows. Another user's id → 404.
- Roles: User, Admin. The first account becomes Admin. Admins manage accounts
  (`/api/v1/admin/users`) but cannot read anyone's data.
- Time zone is per user (`app_settings.time_zone`, IANA id; default `App:TimeZone`).
- Data Protection keys live in the database (cookies survive restarts / scale-out).
- Outside Development: HSTS + HTTPS redirection; CSP and other security headers on every response.

## Config

`src/DayGrid.Api/appsettings.Development.json` has placeholders for the Postgres connection
string and SMTP email settings. Don't commit real credentials — use
`dotnet user-secrets set "Email:Password" "..."` locally instead, or the `EMAIL_*` environment
variables already wired into `docker-compose.yml`.

| Key | Default | Notes |
|---|---|---|
| `App:PublicBaseUrl` | (none) | Absolute site URL used in email links. **Required in Production** (startup fails without it), except the Embedded desktop exe |
| `App:TimeZone` | `Asia/Kolkata` | Default time zone for new accounts |
| `Database:InitializeSchema` | `false` (`true` in Development) | Run schema migrations at startup (External mode) |
| `Email:Mode` | `Smtp` (`Pickup` in Development) | `Pickup` writes `.eml` files instead of sending |
| `Email:PickupDirectory` | (none) | Pickup folder; relative paths resolve against the app's base directory |
| `Email:FromAddress` | `Email:User` | Sender address |
| `Auth:SecurityStampValidationIntervalSeconds` | `60` | How quickly password changes/locks end other sessions |
| `RateLimiting:Auth:PermitLimit` / `WindowSeconds` | `10` / `60` | Auth endpoint rate limit per client IP |

**Azure:** add the app setting `App__PublicBaseUrl=https://<your-site>` (not yet in `infra/main.bicep`).

## Next steps

Follow `DayGrid-Plan.md` section 7 ("Build roadmap") phase by phase from here — Phase 2
(the scheduling engine) is already done, so you're effectively starting around Phase 3/3b
(Checklists + Tasks — Tasks is further along than Checklists right now) with a working Today
page ahead of schedule from Phase 5.
