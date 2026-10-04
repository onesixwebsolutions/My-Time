import { APIRequestContext, expect } from '@playwright/test';

// Small helpers for seeding/verifying data through the real API, plus "now" in the app's
// time zone (Asia/Kolkata — the API's App:TimeZone and the browser context's timezoneId).

export const APP_TZ = 'Asia/Kolkata';

export function uid(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}${Math.random().toString(36).slice(2, 6)}`;
}

function zonedParts(date: Date): Record<string, string> {
  const parts = new Intl.DateTimeFormat('en-CA', {
    timeZone: APP_TZ,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23'
  }).formatToParts(date);
  return Object.fromEntries(parts.map((p) => [p.type, p.value]));
}

/** yyyy-MM-dd for today (+ offset days) in the app time zone. */
export function appDate(offsetDays = 0): string {
  const p = zonedParts(new Date(Date.now() + offsetDays * 86_400_000));
  return `${p['year']}-${p['month']}-${p['day']}`;
}

function hhmm(totalMinutes: number): string {
  const h = Math.floor(totalMinutes / 60);
  const m = totalMinutes % 60;
  return `${String(h).padStart(2, '0')}:${String(m).padStart(2, '0')}`;
}

/**
 * A block window (HH:mm) that contains the current app-zone time with comfortable margins
 * and stays inside one calendar day. Returns null if "now" is too close to midnight for a
 * meaningful window (the caller then skips the now-card assertions).
 */
export function windowAroundNow(): { start: string; end: string } | null {
  const p = zonedParts(new Date());
  const now = Number(p['hour']) * 60 + Number(p['minute']);
  const start = Math.max(0, now - 30);
  const end = Math.min(23 * 60 + 59, now + 90);
  if (now - start < 2 || end - now < 5) return null;
  return { start: hhmm(start), end: hhmm(end) };
}

export async function apiPost<T>(api: APIRequestContext, url: string, data: unknown): Promise<T> {
  const res = await api.post(url, { data });
  expect(res.status(), `POST ${url}: ${await res.text()}`).toBeLessThan(300);
  return (await res.json()) as T;
}

export async function apiGet<T>(api: APIRequestContext, url: string): Promise<T> {
  const res = await api.get(url);
  expect(res.status(), `GET ${url}`).toBe(200);
  return (await res.json()) as T;
}

export async function apiDelete(api: APIRequestContext, url: string): Promise<void> {
  const res = await api.delete(url);
  expect(res.status(), `DELETE ${url}`).toBeLessThan(300);
}

export interface Seeded {
  templateId: string;
  templateName: string;
  blockTitle: string;
  assignmentId: string;
  checklistId: string;
  checklistName: string;
  itemTitle: string;
  futureTaskTitle: string;
}

/**
 * Seeds a realistic "today": a template with one block spanning now (plus a later one),
 * assigned to today's date with a high priority, a checklist with a daily item, and a
 * future task due today.
 */
export async function seedToday(api: APIRequestContext, window: { start: string; end: string }): Promise<Seeded> {
  const templateName = uid('Seed day');
  const template = await apiPost<{ id: string }>(api, '/api/v1/timetable/templates', {
    name: templateName,
    description: 'e2e seeded template',
    dayStart: '00:00:00',
    dayEnd: '23:59:00',
    slotMinutes: 30
  });
  const blockTitle = uid('Deep work');
  await apiPost(api, `/api/v1/timetable/templates/${template.id}/blocks`, {
    title: blockTitle,
    startTime: `${window.start}:00`,
    endTime: `${window.end}:00`,
    category: 'Work',
    color: '#3b82f6',
    location: 'Desk',
    checklistId: null,
    allowOverlap: false,
    notifyAtStart: false,
    sortOrder: 0
  });
  const assignment = await apiPost<{ id: string }>(api, '/api/v1/timetable/assignments', {
    templateId: template.id,
    scope: 'SpecificDate',
    dayOfWeek: null,
    dateFrom: appDate(),
    dateTo: null,
    priority: 100
  });

  const checklistName = uid('Morning routine');
  const checklist = await apiPost<{ id: string }>(api, '/api/v1/checklists', {
    name: checklistName,
    description: null,
    color: '#22c55e',
    icon: null,
    sortOrder: 0
  });
  const itemTitle = uid('Drink water');
  await apiPost(api, `/api/v1/checklists/${checklist.id}/items`, {
    title: itemTitle,
    notes: null,
    priority: 'Normal',
    estimatedMinutes: null,
    anchorType: 'Anytime',
    anchorTime: null,
    windowStart: null,
    windowEnd: null,
    timetableBlockId: null,
    recurrence: {
      type: 'Daily',
      interval: 1,
      daysOfWeek: [],
      dayOfMonth: null,
      nthWeekday: null,
      startDate: null,
      endDate: null,
      exceptionDates: []
    },
    dueDate: null,
    reminderOffsetMinutes: null,
    isActive: true
  });

  const futureTaskTitle = uid('Pay bill');
  await apiPost(api, '/api/v1/future-tasks', {
    title: futureTaskTitle,
    notes: null,
    dueDate: appDate(),
    dueTime: null,
    category: 'Bills',
    priority: 'High',
    reminders: []
  });

  return {
    templateId: template.id,
    templateName,
    blockTitle,
    assignmentId: assignment.id,
    checklistId: checklist.id,
    checklistName,
    itemTitle,
    futureTaskTitle
  };
}

/** Removes the parts of a seed that would otherwise influence later specs' Today page. */
export async function unseedToday(api: APIRequestContext, seeded: Seeded): Promise<void> {
  await api.delete(`/api/v1/timetable/assignments/${seeded.assignmentId}`);
  await api.delete(`/api/v1/timetable/templates/${seeded.templateId}`);
  await api.delete(`/api/v1/checklists/${seeded.checklistId}`);
}
