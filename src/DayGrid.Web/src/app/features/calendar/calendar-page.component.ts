import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  FutureTask,
  FutureTaskRequest,
  FutureTasksApi,
  NotificationChannelName,
  TaskPriorityName
} from '../../core/api/future-tasks.api';

const PRIORITY_COLOR: Record<string, string> = { Low: '#94a3b8', Normal: '#94a3b8', High: '#f59e0b', Critical: '#ef4444' };
const MONTH_NAMES = [
  'January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'
];
const WEEKDAY_LABELS = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

interface DayCell {
  date: Date;
  iso: string;
  day: number;
  inMonth: boolean;
  isToday: boolean;
}

function pad2(n: number): string {
  return n < 10 ? `0${n}` : `${n}`;
}
function toIso(d: Date): string {
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`;
}
function mondayIndex(d: Date): number {
  return (d.getDay() + 6) % 7; // 0 = Monday .. 6 = Sunday
}
function toApiTime(value: string): string | null {
  return value ? `${value}:00` : null;
}

// Month calendar for date-anchored reminders — a thin, date-first view over
// the same FutureTask + Reminder model the Upcoming page manages, per plan's
// "Calendar" nav item. Creating a reminder here always fires exactly at the
// chosen date/time (offsetMinutes 0); Upcoming's richer multi-reminder/offset
// editor stays the tool for tasks that need advance-warning reminders.
@Component({
  selector: 'app-calendar-page',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <h1 class="text-[22px] font-bold tracking-tight text-text">Calendar</h1>
      <div class="ml-auto flex items-center gap-2">
        <button type="button" (click)="prevMonth()" class="grid h-8 w-8 place-items-center rounded-lg border border-border text-muted hover:text-text">‹</button>
        <span class="w-[140px] text-center text-[13.5px] font-semibold text-text">{{ monthLabel() }}</span>
        <button type="button" (click)="nextMonth()" class="grid h-8 w-8 place-items-center rounded-lg border border-border text-muted hover:text-text">›</button>
        <button type="button" (click)="goToday()" class="rounded-lg border border-border px-2.5 py-1.5 text-[11.5px] font-semibold text-muted hover:text-text">Today</button>
      </div>
    </div>

    <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
      <div class="grid grid-cols-7 border-b border-border">
        @for (label of weekdayLabels; track label) {
          <div class="px-2 py-2 text-center text-[10.5px] font-bold uppercase tracking-wider text-muted">{{ label }}</div>
        }
      </div>
      <div class="grid grid-cols-7">
        @for (cell of cells(); track cell.iso) {
          <button
            type="button"
            (click)="selectDay(cell)"
            class="min-h-[84px] border-b border-r border-border p-1.5 text-left transition-colors last:border-r-0 hover:bg-raised2"
            [class.opacity-40]="!cell.inMonth"
            [class.bg-raised2]="selectedIso() === cell.iso"
          >
            <span
              class="grid h-[20px] w-[20px] place-items-center rounded-full text-[11.5px] font-semibold"
              [class.bg-accent]="cell.isToday"
              [class.text-white]="cell.isToday"
              [class.text-text]="!cell.isToday"
            >
              {{ cell.day }}
            </span>
            <div class="mt-1 space-y-0.5">
              @for (t of (tasksByDate().get(cell.iso) ?? []).slice(0, 3); track t.id) {
                <div
                  class="truncate rounded px-1 py-0.5 text-[10px] font-medium"
                  [style.background]="priorityColor(t.priority) + '26'"
                  [style.color]="priorityColor(t.priority)"
                  [class.line-through]="t.status === 'Done' || t.status === 'Cancelled'"
                >
                  {{ t.title }}
                </div>
              }
              @if ((tasksByDate().get(cell.iso)?.length ?? 0) > 3) {
                <div class="px-1 text-[10px] font-medium text-muted">+{{ (tasksByDate().get(cell.iso)?.length ?? 0) - 3 }} more</div>
              }
            </div>
          </button>
        }
      </div>
    </div>

    @if (selectedIso(); as iso) {
      <div class="mt-5 overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[13.5px] font-bold text-text">{{ selectedLabel() }}</h3>
          <button
            type="button"
            (click)="adding = !adding"
            class="ml-auto rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]"
          >
            {{ adding ? 'Cancel' : '+ Add reminder' }}
          </button>
        </div>

        @if (adding) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <div class="grid grid-cols-[1fr_110px_110px] gap-2.5">
              <input
                class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
                placeholder="What should we remind you about?"
                [(ngModel)]="draftTitle"
                name="draftTitle"
              />
              <input
                type="time"
                class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
                [(ngModel)]="draftTime"
                name="draftTime"
              />
              <select
                class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
                [(ngModel)]="draftPriority"
                name="draftPriority"
              >
                <option value="Low">Low</option>
                <option value="Normal">Normal</option>
                <option value="High">High</option>
                <option value="Critical">Critical</option>
              </select>
            </div>
            <div class="mt-2.5 flex items-center gap-4">
              <label class="flex items-center gap-1.5 text-[12.5px] text-text">
                <input type="checkbox" [(ngModel)]="notifyInApp" name="notifyInApp" /> Notify in-app
              </label>
              <label class="flex items-center gap-1.5 text-[12.5px] text-text">
                <input type="checkbox" [(ngModel)]="notifyEmail" name="notifyEmail" /> Notify by email
              </label>
            </div>
            @if (error()) {
              <div class="mt-2 text-[12px] font-medium text-danger">{{ error() }}</div>
            }
            <div class="mt-3 flex justify-end">
              <button type="button" (click)="createReminder(iso)" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">
                Add reminder
              </button>
            </div>
          </div>
        }

        @if ((tasksByDate().get(iso) ?? []).length === 0 && !adding) {
          <div class="px-4 py-8 text-center text-[13px] text-muted">Nothing scheduled for this day.</div>
        } @else {
          @for (t of tasksByDate().get(iso) ?? []; track t.id) {
            <div class="flex items-center gap-3 border-b border-border px-4 py-3 last:border-b-0">
              <span class="h-3.5 w-[3px] flex-none self-stretch rounded" [style.background]="priorityColor(t.priority)"></span>
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium" [class.line-through]="t.status === 'Done' || t.status === 'Cancelled'" [class.text-muted]="t.status === 'Done' || t.status === 'Cancelled'">
                  {{ t.title }}
                </b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  <span>{{ t.dueTime ? t.dueTime.slice(0, 5) : 'All day' }}</span>
                  @if (t.reminders.length) {
                    <span>· 🔔 {{ t.reminders[0].channels }} · {{ t.reminders[0].status }}</span>
                  }
                </div>
              </div>
              @if (t.status === 'Pending' || t.status === 'Deferred') {
                <button type="button" (click)="markDone(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Done</button>
              }
              <button type="button" (click)="remove(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">Delete</button>
            </div>
          }
        }
      </div>
    }
  `
})
export class CalendarPageComponent implements OnInit {
  private readonly api = inject(FutureTasksApi);

  protected readonly weekdayLabels = WEEKDAY_LABELS;
  protected readonly cells = signal<DayCell[]>([]);
  protected readonly tasksByDate = signal<Map<string, FutureTask[]>>(new Map());
  protected readonly selectedIso = signal<string | null>(null);
  protected readonly error = signal<string | null>(null);

  private viewYear: number;
  private viewMonth: number;

  protected adding = false;
  protected draftTitle = '';
  protected draftTime = '';
  protected draftPriority: TaskPriorityName = 'Normal';
  protected notifyInApp = true;
  protected notifyEmail = false;

  constructor() {
    const now = new Date();
    this.viewYear = now.getFullYear();
    this.viewMonth = now.getMonth();
  }

  ngOnInit(): void {
    this.buildGridAndLoad();
  }

  protected monthLabel(): string {
    return `${MONTH_NAMES[this.viewMonth]} ${this.viewYear}`;
  }

  protected selectedLabel(): string {
    const iso = this.selectedIso();
    if (!iso) return '';
    const cell = this.cells().find((c) => c.iso === iso);
    return cell ? cell.date.toLocaleDateString(undefined, { weekday: 'long', month: 'long', day: 'numeric', year: 'numeric' }) : iso;
  }

  protected prevMonth(): void {
    this.viewMonth -= 1;
    if (this.viewMonth < 0) {
      this.viewMonth = 11;
      this.viewYear -= 1;
    }
    this.buildGridAndLoad();
  }

  protected nextMonth(): void {
    this.viewMonth += 1;
    if (this.viewMonth > 11) {
      this.viewMonth = 0;
      this.viewYear += 1;
    }
    this.buildGridAndLoad();
  }

  protected goToday(): void {
    const now = new Date();
    this.viewYear = now.getFullYear();
    this.viewMonth = now.getMonth();
    this.buildGridAndLoad();
    this.selectDay({ iso: toIso(now) } as DayCell);
  }

  private buildGridAndLoad(): void {
    const firstOfMonth = new Date(this.viewYear, this.viewMonth, 1);
    const gridStart = new Date(firstOfMonth);
    gridStart.setDate(gridStart.getDate() - mondayIndex(firstOfMonth));

    const today = toIso(new Date());
    const cells: DayCell[] = [];
    for (let i = 0; i < 42; i++) {
      const d = new Date(gridStart);
      d.setDate(gridStart.getDate() + i);
      const iso = toIso(d);
      cells.push({ date: d, iso, day: d.getDate(), inMonth: d.getMonth() === this.viewMonth, isToday: iso === today });
    }
    this.cells.set(cells);

    const from = cells[0].iso;
    const to = cells[cells.length - 1].iso;
    this.api.list(undefined, from, to).subscribe({
      next: (tasks) => {
        const map = new Map<string, FutureTask[]>();
        for (const t of tasks) {
          const bucket = map.get(t.dueDate) ?? [];
          bucket.push(t);
          map.set(t.dueDate, bucket);
        }
        this.tasksByDate.set(map);
      },
      error: () => this.error.set('Could not load reminders for this month.')
    });
  }

  protected selectDay(cell: DayCell): void {
    this.selectedIso.set(cell.iso);
    this.adding = false;
    this.draftTitle = '';
    this.draftTime = '';
    this.draftPriority = 'Normal';
    this.notifyInApp = true;
    this.notifyEmail = false;
    this.error.set(null);
  }

  protected priorityColor(priority: string): string {
    return PRIORITY_COLOR[priority] ?? '#94a3b8';
  }

  protected createReminder(dueDate: string): void {
    const title = this.draftTitle.trim();
    if (!title) {
      this.error.set('Give the reminder a title.');
      return;
    }
    const channels: NotificationChannelName[] = [];
    if (this.notifyInApp) channels.push('InApp');
    if (this.notifyEmail) channels.push('Email');
    if (channels.length === 0) channels.push('InApp');

    const request: FutureTaskRequest = {
      title,
      dueDate,
      dueTime: toApiTime(this.draftTime),
      priority: this.draftPriority,
      reminders: [{ offsetMinutes: 0, channels }]
    };

    this.api.create(request).subscribe({
      next: (created) => {
        this.tasksByDate.update((map) => {
          const next = new Map(map);
          const bucket = [...(next.get(dueDate) ?? []), created];
          next.set(dueDate, bucket);
          return next;
        });
        this.draftTitle = '';
        this.draftTime = '';
        this.notifyInApp = true;
        this.notifyEmail = false;
        this.adding = false;
        this.error.set(null);
      },
      error: () => this.error.set('Could not create reminder.')
    });
  }

  protected markDone(t: FutureTask): void {
    this.api.setStatus(t.id, 'Done').subscribe({
      next: (updated) => this.replaceTask(updated),
      error: () => this.error.set('Could not update reminder.')
    });
  }

  protected remove(t: FutureTask): void {
    this.api.remove(t.id).subscribe({
      next: () => {
        this.tasksByDate.update((map) => {
          const next = new Map(map);
          const bucket = (next.get(t.dueDate) ?? []).filter((x) => x.id !== t.id);
          next.set(t.dueDate, bucket);
          return next;
        });
      },
      error: () => this.error.set('Could not delete reminder.')
    });
  }

  private replaceTask(updated: FutureTask): void {
    this.tasksByDate.update((map) => {
      const next = new Map(map);
      const bucket = (next.get(updated.dueDate) ?? []).map((x) => (x.id === updated.id ? updated : x));
      next.set(updated.dueDate, bucket);
      return next;
    });
  }
}
