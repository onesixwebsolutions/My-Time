# Why there's (almost) nothing in this folder

DayGrid deliberately does **not** put a full repository/service layer in front of every table.
For an app this size that ceremony buys nothing and costs a file per entity. One rule instead:

- **CRUD-shaped modules — Tasks, Checklists, Timetable, Future tasks, Settings** — talk to
  `AppDbContext` directly from minimal-API endpoint handlers in `DayGrid.Api/Endpoints/*.cs`.
  There is no `ISimpleTaskService` / `SimpleTaskService`, no `IChecklistRepository`, etc. The
  endpoint method *is* the service method: bind → query/mutate `AppDbContext` → map to a DTO →
  return. This keeps the simple modules simple, and keeps `DayGrid.Application` free of a
  reference to `DayGrid.Infrastructure` (which owns `AppDbContext`), preserving the dependency
  direction described in plan section 2.

- **The genuinely complex piece — assembling a single day's plan — gets a real service.**
  `IDayPlanBuilder` (interface, this project, `Scheduling/DayPlanBuilder.cs`) and
  `RecurrenceEngine` (this project, `Scheduling/RecurrenceEngine.cs`) are the only pieces of
  business logic that are non-trivial enough, and reused across enough endpoints
  (`/today`, `/today/now`, `/days/{date}/summary`, `/days/range`, `preview-occurrences`), to
  earn a dedicated abstraction with its own unit tests. `IDayPlanBuilder`'s implementation lives
  in `DayGrid.Infrastructure/Scheduling/DayPlanBuilder.cs` because it needs `AppDbContext`.

Consequence for the Tasks module specifically (plan section 5.5b): `TasksEndpoints.cs` in
`DayGrid.Api` has zero references to `IDayPlanBuilder`, `RecurrenceEngine`, or any
`Reminder`/`NotificationLog` type. That is not an oversight — it is the module's whole point.
It is the simplest endpoint file in the app, on purpose, and it should stay that way.
