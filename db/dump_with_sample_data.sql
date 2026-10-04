--
-- PostgreSQL database dump
--

\restrict KOqPA0f0PWo2RHzKEu5Rn6zxnh9OpYzxaCzm4ESxlZnsOHIXOUzcf6gCesq79Vv

-- Dumped from database version 16.13 (Ubuntu 16.13-0ubuntu0.24.04.1)
-- Dumped by pg_dump version 16.13 (Ubuntu 16.13-0ubuntu0.24.04.1)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: uuid-ossp; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS "uuid-ossp" WITH SCHEMA public;


--
-- Name: EXTENSION "uuid-ossp"; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION "uuid-ossp" IS 'generate universally unique identifiers (UUIDs)';


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: app_settings; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.app_settings (
    id integer DEFAULT 1 NOT NULL,
    time_zone character varying(60) DEFAULT 'Asia/Kolkata'::character varying NOT NULL,
    week_starts_on smallint DEFAULT 1 NOT NULL,
    day_start time without time zone DEFAULT '06:00:00'::time without time zone NOT NULL,
    day_end time without time zone DEFAULT '23:00:00'::time without time zone NOT NULL,
    default_slot_minutes smallint DEFAULT 30 NOT NULL,
    email_enabled boolean DEFAULT true NOT NULL,
    email_to character varying(200),
    daily_digest_time time without time zone DEFAULT '07:00:00'::time without time zone,
    theme character varying(20) DEFAULT 'system'::character varying NOT NULL,
    CONSTRAINT ck_app_settings_singleton CHECK ((id = 1))
);


--
-- Name: checklist_completions; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checklist_completions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    checklist_item_id uuid NOT NULL,
    occurrence_date date NOT NULL,
    completed_at timestamp with time zone DEFAULT now() NOT NULL,
    status smallint DEFAULT 0 NOT NULL,
    note text
);


--
-- Name: checklist_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checklist_items (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    checklist_id uuid NOT NULL,
    title character varying(200) NOT NULL,
    notes text,
    priority smallint DEFAULT 1 NOT NULL,
    estimated_minutes integer,
    anchor_type smallint DEFAULT 0 NOT NULL,
    anchor_time time without time zone,
    window_start time without time zone,
    window_end time without time zone,
    timetable_block_id uuid,
    recurrence jsonb DEFAULT '{"type": "None"}'::jsonb NOT NULL,
    due_date date,
    reminder_offset_minutes integer,
    is_active boolean DEFAULT true NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: checklists; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.checklists (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(120) NOT NULL,
    description text,
    color character varying(9),
    icon character varying(40),
    sort_order integer DEFAULT 0 NOT NULL,
    is_archived boolean DEFAULT false NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: day_overrides; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.day_overrides (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    date date NOT NULL,
    mode smallint DEFAULT 0 NOT NULL,
    template_id uuid,
    note character varying(200)
);


--
-- Name: future_tasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.future_tasks (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    title character varying(200) NOT NULL,
    notes text,
    due_date date NOT NULL,
    due_time time without time zone,
    category character varying(60),
    priority smallint DEFAULT 1 NOT NULL,
    status smallint DEFAULT 0 NOT NULL,
    completed_at timestamp with time zone,
    promote_to_checklist_id uuid,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: notification_log; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.notification_log (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    reminder_id uuid,
    title character varying(200) NOT NULL,
    body text NOT NULL,
    channel smallint DEFAULT 1 NOT NULL,
    created_at_utc timestamp with time zone DEFAULT now() NOT NULL,
    read_at_utc timestamp with time zone,
    error text
);


--
-- Name: reminders; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.reminders (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    future_task_id uuid,
    checklist_item_id uuid,
    offset_minutes integer NOT NULL,
    fire_at_utc timestamp with time zone NOT NULL,
    channels smallint DEFAULT 1 NOT NULL,
    status smallint DEFAULT 0 NOT NULL,
    sent_at_utc timestamp with time zone,
    attempt_count integer DEFAULT 0 NOT NULL,
    CONSTRAINT ck_reminders_exactly_one_owner CHECK (((((future_task_id IS NOT NULL))::integer + ((checklist_item_id IS NOT NULL))::integer) = 1))
);


--
-- Name: simple_tasks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.simple_tasks (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    title character varying(200) NOT NULL,
    notes text,
    priority smallint DEFAULT 1 NOT NULL,
    status smallint DEFAULT 0 NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    completed_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Name: timetable_assignments; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.timetable_assignments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    template_id uuid NOT NULL,
    scope smallint NOT NULL,
    day_of_week smallint,
    date_from date,
    date_to date,
    priority integer DEFAULT 0 NOT NULL
);


--
-- Name: timetable_blocks; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.timetable_blocks (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    template_id uuid NOT NULL,
    title character varying(160) NOT NULL,
    start_time time without time zone NOT NULL,
    end_time time without time zone NOT NULL,
    category smallint DEFAULT 6 NOT NULL,
    color character varying(9),
    location character varying(120),
    checklist_id uuid,
    allow_overlap boolean DEFAULT false NOT NULL,
    notify_at_start boolean DEFAULT false NOT NULL,
    sort_order integer DEFAULT 0 NOT NULL,
    CONSTRAINT ck_timetable_blocks_end_after_start CHECK ((end_time > start_time))
);


--
-- Name: timetable_templates; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.timetable_templates (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(120) NOT NULL,
    description text,
    is_default boolean DEFAULT false NOT NULL,
    day_start time without time zone DEFAULT '06:00:00'::time without time zone NOT NULL,
    day_end time without time zone DEFAULT '23:00:00'::time without time zone NOT NULL,
    slot_minutes smallint DEFAULT 30 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


--
-- Data for Name: app_settings; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.app_settings (id, time_zone, week_starts_on, day_start, day_end, default_slot_minutes, email_enabled, email_to, daily_digest_time, theme) FROM stdin;
1	Asia/Kolkata	1	06:00:00	23:00:00	30	t	onesixwebsolutions@gmail.com	07:00:00	system
\.


--
-- Data for Name: checklist_completions; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.checklist_completions (id, checklist_item_id, occurrence_date, completed_at, status, note) FROM stdin;
bc3ceada-d6d3-409e-9ad5-0dc0dd505608	9ce87467-ccc3-4567-a168-cd2a6cb6a502	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
062ae96c-9740-489c-908a-ea7733f4a707	211d58fa-0713-4336-a49d-1b4431613cec	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
68bd4b4d-a569-418a-9241-f236248a8308	dd0d1f80-41c9-4463-9573-4adc2965a329	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
c66b4d7e-52c8-4a19-93a8-01fad8e9e5e1	93c98c1a-897e-40cb-b28e-458f79a9d0c3	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
6efe638b-8bc1-454b-800f-d652b64b0abb	2dc66cf6-7b3d-4dec-9900-da6fcc8e1655	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
0ba77bf9-de7a-4af4-94aa-82fb5debaf3f	9ed56b41-008f-445e-8e83-312398cffbe8	2026-08-20	2026-08-20 06:03:48.053506+00	0	\N
140577e7-5a30-4967-b455-bf98fcb1e27f	9ce87467-ccc3-4567-a168-cd2a6cb6a502	2026-08-19	2026-08-19 06:03:48.053506+00	0	\N
f34f6f83-ec2d-4ac3-ae5b-e42b58773821	93c98c1a-897e-40cb-b28e-458f79a9d0c3	2026-08-19	2026-08-19 06:03:48.053506+00	0	\N
\.


--
-- Data for Name: checklist_items; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.checklist_items (id, checklist_id, title, notes, priority, estimated_minutes, anchor_type, anchor_time, window_start, window_end, timetable_block_id, recurrence, due_date, reminder_offset_minutes, is_active, sort_order, created_at, updated_at) FROM stdin;
9ce87467-ccc3-4567-a168-cd2a6cb6a502	883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	Take vitamins	\N	1	2	1	07:15:00	\N	\N	\N	{"type": 1, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	10	t	0	2026-08-20 06:03:48.04083+00	2026-08-20 06:03:48.04083+00
211d58fa-0713-4336-a49d-1b4431613cec	883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	Make the bed	\N	0	\N	0	\N	\N	\N	\N	{"type": 1, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	1	2026-08-20 06:03:48.042188+00	2026-08-20 06:03:48.042188+00
dd0d1f80-41c9-4463-9573-4adc2965a329	883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	30 min workout	\N	2	\N	3	\N	\N	\N	fd4f6d4d-e6fe-48ba-b0e3-0c693cfda48a	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5, 6], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	2	2026-08-20 06:03:48.042893+00	2026-08-20 06:03:48.042893+00
35b48631-3919-4a0c-873c-f192a17c5f77	883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	Morning journal	\N	1	10	1	07:40:00	\N	\N	\N	{"type": 1, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	3	2026-08-20 06:03:48.043745+00	2026-08-20 06:03:48.043745+00
5bd9755a-dda8-4872-b285-34f7793ed552	883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	Plan top 3 priorities	\N	1	\N	2	\N	08:00:00	09:00:00	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	4	2026-08-20 06:03:48.044348+00	2026-08-20 06:03:48.044348+00
93c98c1a-897e-40cb-b28e-458f79a9d0c3	81baf705-89bf-4427-afc8-4a380bc4a256	Check overnight deployments	\N	1	\N	1	09:00:00	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	0	2026-08-20 06:03:48.044964+00	2026-08-20 06:03:48.044964+00
2dc66cf6-7b3d-4dec-9900-da6fcc8e1655	81baf705-89bf-4427-afc8-4a380bc4a256	Triage support queue	\N	0	\N	1	09:15:00	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	1	2026-08-20 06:03:48.04562+00	2026-08-20 06:03:48.04562+00
fe741dae-c666-4503-ad31-d2183a22bf65	81baf705-89bf-4427-afc8-4a380bc4a256	Send standup notes	\N	2	\N	1	09:30:00	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	5	t	2	2026-08-20 06:03:48.046316+00	2026-08-20 06:03:48.046316+00
e86fb53a-e8e9-476c-843f-6cae4d60ff90	81baf705-89bf-4427-afc8-4a380bc4a256	Review PR queue	\N	2	20	1	16:30:00	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	10	t	3	2026-08-20 06:03:48.047005+00	2026-08-20 06:03:48.047005+00
83a443d3-8fa4-423d-9013-6c7639e3f2da	81baf705-89bf-4427-afc8-4a380bc4a256	Update sprint board	\N	1	\N	3	\N	\N	\N	01f06d4e-b5e5-4560-90a1-7d2269f60f34	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [1, 2, 3, 4, 5], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	4	2026-08-20 06:03:48.047587+00	2026-08-20 06:03:48.047587+00
fd0ea6f4-a3bd-4f6f-9c7d-f32c07291e22	81baf705-89bf-4427-afc8-4a380bc4a256	Weekly report draft	\N	1	\N	0	\N	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [3], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	5	2026-08-20 06:03:48.048728+00	2026-08-20 06:03:48.048728+00
9ed56b41-008f-445e-8e83-312398cffbe8	f0de9686-a920-4029-b91c-97a35ce80f68	Water the plants	\N	0	\N	0	\N	\N	\N	\N	{"type": 5, "endDate": null, "interval": 3, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	0	2026-08-20 06:03:48.049399+00	2026-08-20 06:03:48.049399+00
f5490cd0-8284-414c-ac37-40b11d5bb737	f0de9686-a920-4029-b91c-97a35ce80f68	Read 20 pages	\N	0	\N	3	\N	\N	\N	0805d3c1-3b08-4e13-9248-f25c98efabb3	{"type": 1, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	1	2026-08-20 06:03:48.049928+00	2026-08-20 06:03:48.049928+00
5b92e993-9ec1-4b9e-bda4-0f4838ed80c9	f0de9686-a920-4029-b91c-97a35ce80f68	Prep tomorrow's clothes	\N	0	\N	1	21:30:00	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [0, 1, 2, 3, 4], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	2	2026-08-20 06:03:48.050745+00	2026-08-20 06:03:48.050745+00
a7fec9c4-9cbe-4f6d-89ef-85af8aad726c	0a9c9b71-71c6-479c-9b4f-109cb8287048	Grocery run	\N	1	\N	0	\N	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [6], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	0	2026-08-20 06:03:48.051331+00	2026-08-20 06:03:48.051331+00
62769e63-f454-467f-b552-59c946b62465	0a9c9b71-71c6-479c-9b4f-109cb8287048	Deep clean	\N	1	\N	0	\N	\N	\N	\N	{"type": 4, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [], "nthWeekday": {"nth": 1, "dayOfWeek": 0}, "exceptionDates": []}	\N	\N	t	1	2026-08-20 06:03:48.051832+00	2026-08-20 06:03:48.051832+00
83e3148f-e7ee-4df4-aaba-81290edb0c74	0a9c9b71-71c6-479c-9b4f-109cb8287048	Pay rent	\N	2	\N	0	\N	\N	\N	\N	{"type": 3, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": 5, "daysOfWeek": [], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	2	2026-08-20 06:03:48.052395+00	2026-08-20 06:03:48.052395+00
761a03be-4047-46af-a02e-e118b0c420c3	0a9c9b71-71c6-479c-9b4f-109cb8287048	Take out recycling	\N	0	\N	0	\N	\N	\N	\N	{"type": 2, "endDate": null, "interval": 1, "startDate": "2026-08-01", "dayOfMonth": null, "daysOfWeek": [2, 5], "nthWeekday": null, "exceptionDates": []}	\N	\N	t	3	2026-08-20 06:03:48.05297+00	2026-08-20 06:03:48.05297+00
\.


--
-- Data for Name: checklists; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.checklists (id, name, description, color, icon, sort_order, is_archived, created_at, updated_at) FROM stdin;
883d18ab-e5a3-4f9c-9890-58a99f6ddd0c	Morning Routine	The first hour, on autopilot.	#f59e0b	sunrise	0	f	2026-08-20 06:03:48.019173+00	2026-08-20 06:03:48.019173+00
81baf705-89bf-4427-afc8-4a380bc4a256	Work	Recurring work hygiene — not project tasks.	#3b82f6	briefcase	1	f	2026-08-20 06:03:48.021531+00	2026-08-20 06:03:48.021531+00
f0de9686-a920-4029-b91c-97a35ce80f68	Evening Wind-down	Close the day out deliberately.	#6366f1	moon	2	f	2026-08-20 06:03:48.022191+00	2026-08-20 06:03:48.022191+00
0a9c9b71-71c6-479c-9b4f-109cb8287048	Home & Errands	Weekly and monthly household upkeep.	#22c55e	home	3	f	2026-08-20 06:03:48.022799+00	2026-08-20 06:03:48.022799+00
\.


--
-- Data for Name: day_overrides; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.day_overrides (id, date, mode, template_id, note) FROM stdin;
c5322ae2-178c-4784-b433-0a33976bda60	2026-08-29	0	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Travel day — lighter schedule
\.


--
-- Data for Name: future_tasks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.future_tasks (id, title, notes, due_date, due_time, category, priority, status, completed_at, promote_to_checklist_id, created_at, updated_at) FROM stdin;
29f34fb2-f293-451c-b263-40f3b3ec4de8	Pay electricity bill	Before the due date to avoid a late fee.	2026-08-20	18:00:00	Personal	2	0	\N	\N	2026-08-20 06:03:48.054973+00	2026-08-20 06:03:48.054973+00
ee376523-b5f3-4aea-9d97-18731e1b2d73	Call the dentist to reschedule	\N	2026-08-20	\N	Health	1	0	\N	\N	2026-08-20 06:03:48.057914+00	2026-08-20 06:03:48.057914+00
d03c13fc-df39-49fb-b627-1a0ccc787f45	Submit quarterly tax declaration	\N	2026-08-21	14:00:00	Finance	2	0	\N	\N	2026-08-20 06:03:48.059254+00	2026-08-20 06:03:48.059254+00
567f35a5-36aa-41f0-8588-c89d2ea316a1	Renew passport	Appointment slot booked at PSK.	2026-09-13	11:00:00	Personal	3	0	\N	\N	2026-08-20 06:03:48.060637+00	2026-08-20 06:03:48.060637+00
572d76ed-af44-445f-8317-67b33fe055ba	Domain & hosting renewal	\N	2026-10-03	\N	Work	1	0	\N	\N	2026-08-20 06:03:48.062179+00	2026-08-20 06:03:48.062179+00
\.


--
-- Data for Name: notification_log; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.notification_log (id, reminder_id, title, body, channel, created_at_utc, read_at_utc, error) FROM stdin;
\.


--
-- Data for Name: reminders; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.reminders (id, future_task_id, checklist_item_id, offset_minutes, fire_at_utc, channels, status, sent_at_utc, attempt_count) FROM stdin;
75276bc3-4039-4d62-be26-18d97bc25648	29f34fb2-f293-451c-b263-40f3b3ec4de8	\N	30	2026-08-20 12:00:00+00	3	0	\N	0
5f5b12a7-ab5a-42f7-a50a-5b8def8941f7	ee376523-b5f3-4aea-9d97-18731e1b2d73	\N	0	2026-08-20 03:30:00+00	1	0	\N	0
d7e32b1d-dc95-4df6-868e-f4babe0a4f0d	d03c13fc-df39-49fb-b627-1a0ccc787f45	\N	1440	2026-08-20 08:30:00+00	3	0	\N	0
c65c49dd-f011-416f-bbbe-0e3dcdb1ee79	d03c13fc-df39-49fb-b627-1a0ccc787f45	\N	60	2026-08-21 07:30:00+00	3	0	\N	0
914542f1-afea-4d9b-9c1e-cd832dc5857d	567f35a5-36aa-41f0-8588-c89d2ea316a1	\N	10080	2026-09-06 05:30:00+00	3	0	\N	0
a4392808-4c5e-4871-9a56-d7e0736c6c3a	567f35a5-36aa-41f0-8588-c89d2ea316a1	\N	1440	2026-09-12 05:30:00+00	3	0	\N	0
21aed412-c5e4-4325-b3b1-107af77579c4	572d76ed-af44-445f-8317-67b33fe055ba	\N	20160	2026-09-19 05:30:00+00	3	0	\N	0
\.


--
-- Data for Name: simple_tasks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.simple_tasks (id, title, notes, priority, status, sort_order, completed_at, created_at, updated_at) FROM stdin;
f32a0b91-a03b-4e1d-8cd5-e0e09a21ea2c	Compare health insurance plans	\N	3	0	0	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
33c5bf50-ef54-47c2-8b92-1b237b03ad00	Research standing desks	\N	2	0	1	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
4a45ff00-c215-4554-968a-4f1cda824d13	Sort out old photo backups	\N	1	0	2	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
4a539ebe-a1fa-45af-9a5e-e1207b748f70	Look into a weekend trek	\N	0	0	3	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
be5b5ddf-8c9d-420f-b466-508d293d27cd	Read up on EF Core migrations	\N	2	0	4	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
44ebba66-90ad-4937-9273-82754c0efaa7	Declutter the garage	\N	1	0	5	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
948758ff-6799-4302-9ab6-4f474656b3e3	Try that new ramen place	\N	0	0	6	\N	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
58d2e7eb-19fe-4d47-9357-c537984798ac	Cancel unused streaming subscription	\N	1	1	7	2026-08-18 06:03:48.063525+00	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
d55b1704-df8a-4aab-86b9-2d35fb06fca9	Book eye test	\N	1	1	8	2026-08-15 06:03:48.063525+00	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
91b29484-8035-4c3d-9556-b876c8145a1f	Update resume	\N	1	1	9	2026-08-11 06:03:48.063525+00	2026-08-20 06:03:48.063525+00	2026-08-20 06:03:48.063525+00
\.


--
-- Data for Name: timetable_assignments; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.timetable_assignments (id, template_id, scope, day_of_week, date_from, date_to, priority) FROM stdin;
92e208e0-6402-4dd7-a84e-91be147cc5ec	078d9b04-48d3-4854-97b5-50467660e3fc	0	1	\N	\N	0
3a4542bc-70ed-43b9-9c62-3fef504683e2	078d9b04-48d3-4854-97b5-50467660e3fc	0	2	\N	\N	0
54bff068-61e0-4c30-a339-09a652479cdc	078d9b04-48d3-4854-97b5-50467660e3fc	0	3	\N	\N	0
ba752dd0-a994-4b65-b260-b6fdb9c07162	078d9b04-48d3-4854-97b5-50467660e3fc	0	4	\N	\N	0
5d9f02c2-6f80-4976-95de-815e55c9e977	078d9b04-48d3-4854-97b5-50467660e3fc	0	5	\N	\N	0
d7f7f6ab-cb4c-4e64-99e0-84939ec94d66	ba655fbc-d565-4540-a8c8-c6307b89f3cd	0	0	\N	\N	0
2ba9a2b5-f80e-46b0-8e97-2f5054cfeabf	ba655fbc-d565-4540-a8c8-c6307b89f3cd	0	6	\N	\N	0
\.


--
-- Data for Name: timetable_blocks; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.timetable_blocks (id, template_id, title, start_time, end_time, category, color, location, checklist_id, allow_overlap, notify_at_start, sort_order) FROM stdin;
fd4f6d4d-e6fe-48ba-b0e3-0c693cfda48a	078d9b04-48d3-4854-97b5-50467660e3fc	Workout	06:00:00	07:00:00	2	#22c55e	Home gym	\N	f	f	0
489c3851-a8c1-4746-9bda-cd18d6a6cab0	078d9b04-48d3-4854-97b5-50467660e3fc	Breakfast + Reading	07:00:00	08:00:00	1	\N	\N	\N	f	f	1
38932075-d659-4a29-ac65-9a5d2872ed47	078d9b04-48d3-4854-97b5-50467660e3fc	Commute	08:00:00	09:00:00	4	\N	Metro	\N	f	f	2
e121f1b2-616a-4a58-a726-494986532f90	078d9b04-48d3-4854-97b5-50467660e3fc	Deep Work — Project Alpha	09:00:00	12:30:00	0	#3b82f6	\N	81baf705-89bf-4427-afc8-4a380bc4a256	f	f	3
84dbb982-8d65-46f3-b350-9dfc8d8f6950	078d9b04-48d3-4854-97b5-50467660e3fc	Lunch	12:30:00	13:15:00	4	\N	\N	\N	f	f	4
cb739e79-efc8-4f7d-9bb0-1a136c3ab153	078d9b04-48d3-4854-97b5-50467660e3fc	Meetings & Reviews	13:15:00	15:00:00	0	\N	Conf room	\N	f	f	5
845ef743-bc66-4fd8-8209-8db97a3dd032	078d9b04-48d3-4854-97b5-50467660e3fc	Learning — .NET / Angular	15:00:00	16:30:00	3	\N	\N	\N	f	f	6
01f06d4e-b5e5-4560-90a1-7d2269f60f34	078d9b04-48d3-4854-97b5-50467660e3fc	Admin & Inbox	16:30:00	18:00:00	0	\N	\N	\N	f	f	7
81c3972b-d2c8-4962-997e-62d7d0e4001b	078d9b04-48d3-4854-97b5-50467660e3fc	Gym / Errands	18:00:00	19:00:00	2	\N	\N	\N	f	f	8
544e0b0b-2bea-401c-a75c-ad06525d5f8d	078d9b04-48d3-4854-97b5-50467660e3fc	Dinner + Family	19:00:00	20:00:00	1	\N	\N	\N	f	f	9
0805d3c1-3b08-4e13-9248-f25c98efabb3	078d9b04-48d3-4854-97b5-50467660e3fc	Wind-down & Journal	20:00:00	22:00:00	1	#6366f1	\N	f0de9686-a920-4029-b91c-97a35ce80f68	f	f	10
787938c5-e8c9-41b3-9b8a-db789c0e9cf5	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Slow Breakfast	07:00:00	08:30:00	1	\N	\N	\N	f	f	0
203887f3-1c80-44a0-8541-51dabc0f52a8	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Grocery Run	09:00:00	10:00:00	6	\N	Local market	\N	f	f	1
2a969278-08ad-466f-a2dd-0a6fd3ec3836	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Long Workout	10:00:00	11:30:00	2	\N	\N	\N	f	f	2
73c913b5-27b6-4bd6-885b-9f75618e0ae3	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Free Time	11:30:00	18:00:00	1	\N	\N	\N	f	f	3
fa4045c9-b6a1-46a7-889e-a050c0bfd7a3	ba655fbc-d565-4540-a8c8-c6307b89f3cd	Family Dinner	19:00:00	20:30:00	1	\N	\N	\N	f	f	4
\.


--
-- Data for Name: timetable_templates; Type: TABLE DATA; Schema: public; Owner: -
--

COPY public.timetable_templates (id, name, description, is_default, day_start, day_end, slot_minutes, created_at, updated_at) FROM stdin;
078d9b04-48d3-4854-97b5-50467660e3fc	Weekday	Mon–Fri default shape.	t	06:00:00	23:00:00	30	2026-08-20 06:03:48.023372+00	2026-08-20 06:03:48.023372+00
ba655fbc-d565-4540-a8c8-c6307b89f3cd	Weekend	Slower mornings, more free time.	f	07:00:00	23:00:00	30	2026-08-20 06:03:48.024264+00	2026-08-20 06:03:48.024264+00
\.


--
-- Name: app_settings app_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.app_settings
    ADD CONSTRAINT app_settings_pkey PRIMARY KEY (id);


--
-- Name: checklist_completions checklist_completions_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklist_completions
    ADD CONSTRAINT checklist_completions_pkey PRIMARY KEY (id);


--
-- Name: checklist_items checklist_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklist_items
    ADD CONSTRAINT checklist_items_pkey PRIMARY KEY (id);


--
-- Name: checklists checklists_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklists
    ADD CONSTRAINT checklists_pkey PRIMARY KEY (id);


--
-- Name: day_overrides day_overrides_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.day_overrides
    ADD CONSTRAINT day_overrides_pkey PRIMARY KEY (id);


--
-- Name: future_tasks future_tasks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.future_tasks
    ADD CONSTRAINT future_tasks_pkey PRIMARY KEY (id);


--
-- Name: notification_log notification_log_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_log
    ADD CONSTRAINT notification_log_pkey PRIMARY KEY (id);


--
-- Name: reminders reminders_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reminders
    ADD CONSTRAINT reminders_pkey PRIMARY KEY (id);


--
-- Name: simple_tasks simple_tasks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.simple_tasks
    ADD CONSTRAINT simple_tasks_pkey PRIMARY KEY (id);


--
-- Name: timetable_assignments timetable_assignments_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_assignments
    ADD CONSTRAINT timetable_assignments_pkey PRIMARY KEY (id);


--
-- Name: timetable_blocks timetable_blocks_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_blocks
    ADD CONSTRAINT timetable_blocks_pkey PRIMARY KEY (id);


--
-- Name: timetable_templates timetable_templates_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_templates
    ADD CONSTRAINT timetable_templates_pkey PRIMARY KEY (id);


--
-- Name: ix_checklist_items_anchor; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_checklist_items_anchor ON public.checklist_items USING btree (anchor_type, anchor_time);


--
-- Name: ix_checklist_items_checklist_active; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_checklist_items_checklist_active ON public.checklist_items USING btree (checklist_id, is_active);


--
-- Name: ix_checklist_items_recurrence_gin; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_checklist_items_recurrence_gin ON public.checklist_items USING gin (recurrence);


--
-- Name: ix_future_tasks_status_due; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_future_tasks_status_due ON public.future_tasks USING btree (status, due_date);


--
-- Name: ix_notification_log_created_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_log_created_at ON public.notification_log USING btree (created_at_utc);


--
-- Name: ix_notification_log_read_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_notification_log_read_at ON public.notification_log USING btree (read_at_utc);


--
-- Name: ix_reminders_status_fire_at; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_reminders_status_fire_at ON public.reminders USING btree (status, fire_at_utc);


--
-- Name: ix_simple_tasks_status_sort; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_simple_tasks_status_sort ON public.simple_tasks USING btree (status, sort_order);


--
-- Name: ix_timetable_assignments_scope_range; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_timetable_assignments_scope_range ON public.timetable_assignments USING btree (scope, date_from, date_to);


--
-- Name: ix_timetable_assignments_scope_weekday; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_timetable_assignments_scope_weekday ON public.timetable_assignments USING btree (scope, day_of_week);


--
-- Name: ix_timetable_blocks_template_start; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_timetable_blocks_template_start ON public.timetable_blocks USING btree (template_id, start_time);


--
-- Name: ux_checklist_completions_item_date; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_checklist_completions_item_date ON public.checklist_completions USING btree (checklist_item_id, occurrence_date);


--
-- Name: ux_day_overrides_date; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_day_overrides_date ON public.day_overrides USING btree (date);


--
-- Name: checklist_completions checklist_completions_checklist_item_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklist_completions
    ADD CONSTRAINT checklist_completions_checklist_item_id_fkey FOREIGN KEY (checklist_item_id) REFERENCES public.checklist_items(id) ON DELETE CASCADE;


--
-- Name: checklist_items checklist_items_checklist_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklist_items
    ADD CONSTRAINT checklist_items_checklist_id_fkey FOREIGN KEY (checklist_id) REFERENCES public.checklists(id) ON DELETE CASCADE;


--
-- Name: day_overrides day_overrides_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.day_overrides
    ADD CONSTRAINT day_overrides_template_id_fkey FOREIGN KEY (template_id) REFERENCES public.timetable_templates(id) ON DELETE SET NULL;


--
-- Name: checklist_items fk_checklist_items_timetable_block; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.checklist_items
    ADD CONSTRAINT fk_checklist_items_timetable_block FOREIGN KEY (timetable_block_id) REFERENCES public.timetable_blocks(id) ON DELETE SET NULL;


--
-- Name: future_tasks future_tasks_promote_to_checklist_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.future_tasks
    ADD CONSTRAINT future_tasks_promote_to_checklist_id_fkey FOREIGN KEY (promote_to_checklist_id) REFERENCES public.checklists(id) ON DELETE SET NULL;


--
-- Name: notification_log notification_log_reminder_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.notification_log
    ADD CONSTRAINT notification_log_reminder_id_fkey FOREIGN KEY (reminder_id) REFERENCES public.reminders(id) ON DELETE SET NULL;


--
-- Name: reminders reminders_checklist_item_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reminders
    ADD CONSTRAINT reminders_checklist_item_id_fkey FOREIGN KEY (checklist_item_id) REFERENCES public.checklist_items(id) ON DELETE CASCADE;


--
-- Name: reminders reminders_future_task_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.reminders
    ADD CONSTRAINT reminders_future_task_id_fkey FOREIGN KEY (future_task_id) REFERENCES public.future_tasks(id) ON DELETE CASCADE;


--
-- Name: timetable_assignments timetable_assignments_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_assignments
    ADD CONSTRAINT timetable_assignments_template_id_fkey FOREIGN KEY (template_id) REFERENCES public.timetable_templates(id) ON DELETE CASCADE;


--
-- Name: timetable_blocks timetable_blocks_checklist_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_blocks
    ADD CONSTRAINT timetable_blocks_checklist_id_fkey FOREIGN KEY (checklist_id) REFERENCES public.checklists(id) ON DELETE SET NULL;


--
-- Name: timetable_blocks timetable_blocks_template_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.timetable_blocks
    ADD CONSTRAINT timetable_blocks_template_id_fkey FOREIGN KEY (template_id) REFERENCES public.timetable_templates(id) ON DELETE CASCADE;


--
-- PostgreSQL database dump complete
--

\unrestrict KOqPA0f0PWo2RHzKEu5Rn6zxnh9OpYzxaCzm4ESxlZnsOHIXOUzcf6gCesq79Vv

