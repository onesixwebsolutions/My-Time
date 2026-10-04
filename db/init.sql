-- DayGrid — standalone reference schema (PostgreSQL 16)
--
-- This is NOT an EF Core migration. It is a hand-written DDL script matching plan section 4
-- exactly, meant to be run directly against Postgres (e.g. mounted into the docker-compose
-- Postgres container's /docker-entrypoint-initdb.d/) so there's a working schema before
-- `dotnet ef` tooling is available. Once EF migrations exist, treat those as the source of
-- truth for schema evolution and keep this file as a quick-start / documentation artifact.

-- No extensions needed: gen_random_uuid() is built into PostgreSQL 13+. (uuid-ossp/pgcrypto
-- were dropped — Azure Database for PostgreSQL rejects CREATE EXTENSION unless allow-listed.)

-- ---------------------------------------------------------------------------
-- checklists
-- ---------------------------------------------------------------------------
CREATE TABLE checklists (
    id           uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name         varchar(120) NOT NULL,
    description  text NULL,
    color        varchar(9) NULL,
    icon         varchar(40) NULL,
    sort_order   int NOT NULL DEFAULT 0,
    is_archived  bool NOT NULL DEFAULT false,
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- checklist_items
-- ---------------------------------------------------------------------------
CREATE TABLE checklist_items (
    id                       uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    checklist_id             uuid NOT NULL REFERENCES checklists (id) ON DELETE CASCADE,
    title                    varchar(200) NOT NULL,
    notes                    text NULL,
    priority                 smallint NOT NULL DEFAULT 1,   -- 0 Low · 1 Normal · 2 High · 3 Critical
    estimated_minutes        int NULL,
    anchor_type              smallint NOT NULL DEFAULT 0,   -- 0 Anytime · 1 FixedTime · 2 TimeWindow · 3 LinkedToBlock
    anchor_time              time NULL,
    window_start             time NULL,
    window_end               time NULL,
    timetable_block_id       uuid NULL,                     -- FK added below, after timetable_blocks exists
    recurrence               jsonb NOT NULL DEFAULT '{"type":"None"}',
    due_date                 date NULL,
    reminder_offset_minutes  int NULL,
    is_active                bool NOT NULL DEFAULT true,
    sort_order               int NOT NULL DEFAULT 0,
    created_at                timestamptz NOT NULL DEFAULT now(),
    updated_at                timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_checklist_items_checklist_active ON checklist_items (checklist_id, is_active);
CREATE INDEX ix_checklist_items_anchor ON checklist_items (anchor_type, anchor_time);
CREATE INDEX ix_checklist_items_recurrence_gin ON checklist_items USING gin (recurrence);

-- ---------------------------------------------------------------------------
-- checklist_completions
-- ---------------------------------------------------------------------------
CREATE TABLE checklist_completions (
    id                  uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    checklist_item_id   uuid NOT NULL REFERENCES checklist_items (id) ON DELETE CASCADE,
    occurrence_date     date NOT NULL,
    completed_at        timestamptz NOT NULL DEFAULT now(),
    status               smallint NOT NULL DEFAULT 0,        -- 0 Done · 1 Skipped · 2 Partial
    note                text NULL
);

CREATE UNIQUE INDEX ux_checklist_completions_item_date ON checklist_completions (checklist_item_id, occurrence_date);

-- ---------------------------------------------------------------------------
-- timetable_templates
-- ---------------------------------------------------------------------------
CREATE TABLE timetable_templates (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name          varchar(120) NOT NULL,
    description   text NULL,
    is_default    bool NOT NULL DEFAULT false,
    day_start     time NOT NULL DEFAULT '06:00',
    day_end       time NOT NULL DEFAULT '23:00',
    slot_minutes  smallint NOT NULL DEFAULT 30,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

-- ---------------------------------------------------------------------------
-- timetable_blocks
-- ---------------------------------------------------------------------------
CREATE TABLE timetable_blocks (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id       uuid NOT NULL REFERENCES timetable_templates (id) ON DELETE CASCADE,
    title             varchar(160) NOT NULL,
    start_time        time NOT NULL,
    end_time          time NOT NULL,
    category          smallint NOT NULL DEFAULT 6,  -- 0 Work · 1 Personal · 2 Health · 3 Learning · 4 Break · 5 Sleep · 6 Other
    color             varchar(9) NULL,
    location          varchar(120) NULL,
    checklist_id      uuid NULL REFERENCES checklists (id) ON DELETE SET NULL,
    allow_overlap     bool NOT NULL DEFAULT false,
    notify_at_start   bool NOT NULL DEFAULT false,
    sort_order        int NOT NULL DEFAULT 0,
    CONSTRAINT ck_timetable_blocks_end_after_start CHECK (end_time > start_time)
);

CREATE INDEX ix_timetable_blocks_template_start ON timetable_blocks (template_id, start_time);

-- Now that timetable_blocks exists, add the deferred FK from checklist_items.
ALTER TABLE checklist_items
    ADD CONSTRAINT fk_checklist_items_timetable_block
    FOREIGN KEY (timetable_block_id) REFERENCES timetable_blocks (id) ON DELETE SET NULL;

-- ---------------------------------------------------------------------------
-- timetable_assignments
-- ---------------------------------------------------------------------------
CREATE TABLE timetable_assignments (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    template_id   uuid NOT NULL REFERENCES timetable_templates (id) ON DELETE CASCADE,
    scope         smallint NOT NULL,   -- 0 Weekday · 1 SpecificDate · 2 DateRange
    day_of_week   smallint NULL,       -- 0=Sun ... 6=Sat
    date_from     date NULL,
    date_to       date NULL,
    priority      int NOT NULL DEFAULT 0
);

CREATE INDEX ix_timetable_assignments_scope_weekday ON timetable_assignments (scope, day_of_week);
CREATE INDEX ix_timetable_assignments_scope_range ON timetable_assignments (scope, date_from, date_to);

-- ---------------------------------------------------------------------------
-- day_overrides
-- ---------------------------------------------------------------------------
CREATE TABLE day_overrides (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    date          date NOT NULL,
    mode          smallint NOT NULL DEFAULT 0,   -- 0 UseTemplate · 1 RestDay · 2 CustomOnly
    template_id   uuid NULL REFERENCES timetable_templates (id) ON DELETE SET NULL,
    note          varchar(200) NULL
);

CREATE UNIQUE INDEX ux_day_overrides_date ON day_overrides (date);

-- ---------------------------------------------------------------------------
-- future_tasks
-- ---------------------------------------------------------------------------
CREATE TABLE future_tasks (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    title                       varchar(200) NOT NULL,
    notes                       text NULL,
    due_date                    date NOT NULL,
    due_time                    time NULL,
    category                    varchar(60) NULL,
    priority                    smallint NOT NULL DEFAULT 1,
    status                      smallint NOT NULL DEFAULT 0,  -- 0 Pending · 1 Done · 2 Cancelled · 3 Deferred
    completed_at                timestamptz NULL,
    promote_to_checklist_id     uuid NULL REFERENCES checklists (id) ON DELETE SET NULL,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    updated_at                  timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_future_tasks_status_due ON future_tasks (status, due_date);

-- ---------------------------------------------------------------------------
-- reminders
-- ---------------------------------------------------------------------------
CREATE TABLE reminders (
    id                   uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    future_task_id       uuid NULL REFERENCES future_tasks (id) ON DELETE CASCADE,
    checklist_item_id    uuid NULL REFERENCES checklist_items (id) ON DELETE CASCADE,
    offset_minutes       int NOT NULL,
    fire_at_utc          timestamptz NOT NULL,
    channels             smallint NOT NULL DEFAULT 1,  -- bit flags: 1 InApp · 2 Email
    status               smallint NOT NULL DEFAULT 0,  -- 0 Scheduled · 1 Sent · 2 Failed · 3 Cancelled
    sent_at_utc          timestamptz NULL,
    attempt_count        int NOT NULL DEFAULT 0,
    CONSTRAINT ck_reminders_exactly_one_owner CHECK (
        (future_task_id IS NOT NULL)::int + (checklist_item_id IS NOT NULL)::int = 1
    )
);

CREATE INDEX ix_reminders_status_fire_at ON reminders (status, fire_at_utc);

-- ---------------------------------------------------------------------------
-- notification_log
-- ---------------------------------------------------------------------------
CREATE TABLE notification_log (
    id                uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    reminder_id       uuid NULL REFERENCES reminders (id) ON DELETE SET NULL,
    title             varchar(200) NOT NULL,
    body              text NOT NULL,
    channel           smallint NOT NULL DEFAULT 1,
    created_at_utc    timestamptz NOT NULL DEFAULT now(),
    read_at_utc       timestamptz NULL,
    error             text NULL
);

CREATE INDEX ix_notification_log_created_at ON notification_log (created_at_utc);
CREATE INDEX ix_notification_log_read_at ON notification_log (read_at_utc);

-- ---------------------------------------------------------------------------
-- app_settings — single row
-- ---------------------------------------------------------------------------
CREATE TABLE app_settings (
    id                     int PRIMARY KEY DEFAULT 1,
    time_zone              varchar(60) NOT NULL DEFAULT 'Asia/Kolkata',
    week_starts_on         smallint NOT NULL DEFAULT 1,   -- 1 = Monday
    day_start              time NOT NULL DEFAULT '06:00',
    day_end                time NOT NULL DEFAULT '23:00',
    default_slot_minutes   smallint NOT NULL DEFAULT 30,
    email_enabled          bool NOT NULL DEFAULT true,
    email_to               varchar(200) NULL,
    daily_digest_time      time NULL DEFAULT '07:00',
    theme                  varchar(20) NOT NULL DEFAULT 'system',
    CONSTRAINT ck_app_settings_singleton CHECK (id = 1)
);

INSERT INTO app_settings (id, time_zone, week_starts_on, day_start, day_end, default_slot_minutes, email_enabled, email_to, daily_digest_time, theme)
VALUES (1, 'Asia/Kolkata', 1, '06:00', '23:00', 30, true, 'onesixwebsolutions@gmail.com', '07:00', 'system')
ON CONFLICT (id) DO NOTHING;

-- ---------------------------------------------------------------------------
-- simple_tasks — the standalone Tasks section (plan section 4.2b). Deliberately has no foreign
-- keys to, or from, any other table in this schema.
-- ---------------------------------------------------------------------------
CREATE TABLE simple_tasks (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    title         varchar(200) NOT NULL,
    notes         text NULL,
    priority      smallint NOT NULL DEFAULT 1,
    status        smallint NOT NULL DEFAULT 0,  -- 0 Open · 1 Done
    sort_order    int NOT NULL DEFAULT 0,
    completed_at  timestamptz NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_simple_tasks_status_sort ON simple_tasks (status, sort_order);

-- ---------------------------------------------------------------------------
-- constant_expenses — fixed monthly expenses (rent, subscriptions, EMIs). Amount repeats every
-- month; is_active lets one be paused/cancelled without deleting its history.
-- ---------------------------------------------------------------------------
CREATE TABLE constant_expenses (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    name          varchar(160) NOT NULL,
    amount        numeric(12,2) NOT NULL,
    category      varchar(60) NULL,
    day_of_month  smallint NULL,
    notes         text NULL,
    is_active     bool NOT NULL DEFAULT true,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_constant_expenses_active ON constant_expenses (is_active);

-- ---------------------------------------------------------------------------
-- varying_expenses — one-off/irregular expenses tied to a specific date.
-- ---------------------------------------------------------------------------
CREATE TABLE varying_expenses (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    title         varchar(160) NOT NULL,
    amount        numeric(12,2) NOT NULL,
    category      varchar(60) NULL,
    expense_date  date NOT NULL,
    notes         text NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_varying_expenses_date ON varying_expenses (expense_date);

-- ---------------------------------------------------------------------------
-- completed_spends — "My Spends": a running log of money actually spent, entered as you go.
-- Distinct from varying_expenses — surfaced as its own list + its own "My Spends Completed
-- This Month" total on the Expenses page.
-- ---------------------------------------------------------------------------
CREATE TABLE completed_spends (
    id            uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    title         varchar(160) NOT NULL,
    amount        numeric(12,2) NOT NULL,
    category      varchar(60) NULL,
    spend_date    date NOT NULL,
    notes         text NULL,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now()
);

CREATE INDEX ix_completed_spends_date ON completed_spends (spend_date);
