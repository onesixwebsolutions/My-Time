import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  FutureTask,
  FutureTaskRequest,
  FutureTaskStatusName,
  FutureTasksApi,
  NotificationChannelName,
  ReminderRequest,
  TaskPriorityName
} from '../../core/api/future-tasks.api';
import { FutureTaskFieldsComponent } from './future-task-fields.component';

const PRIORITY_COLOR: Record<string, string> = { Low: '#94a3b8', Normal: '#94a3b8', High: '#f59e0b', Critical: '#ef4444' };
const STATUS_COLOR: Record<string, string> = { Pending: '#94a3b8', Done: '#22c55e', Cancelled: '#ef4444', Deferred: '#f59e0b' };

function toTimeInput(value: string | null): string {
  return value ? value.slice(0, 5) : '';
}
function toApiTime(value: string): string | null {
  return value ? `${value}:00` : null;
}

interface DraftReminder {
  offsetMinutes: number;
  channels: NotificationChannelName[];
}

// Full CRUD for the standalone Future Tasks module: create/edit/delete a
// task, change its status (Pending/Done/Cancelled/Deferred), defer its due
// date, and manage its reminders — mirrors FutureTasksEndpoints.cs.
@Component({
  selector: 'app-upcoming-page',
  imports: [FormsModule, FutureTaskFieldsComponent],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Upcoming</h1>
        <p class="mt-1 text-[13px] text-muted">One-off future tasks with reminders.</p>
      </div>
      <button
        type="button"
        (click)="creating = !creating; editingId = null"
        class="ml-auto rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
      >
        {{ creating ? 'Cancel' : '+ New task' }}
      </button>
    </div>

    <div class="mb-4 flex flex-wrap gap-2">
      @for (opt of statusFilters; track opt.value) {
        <button
          type="button"
          (click)="setFilter(opt.value)"
          class="rounded-full border px-2.5 py-1 text-[11.5px] font-semibold transition-colors"
          [class.border-accent]="filter === opt.value"
          [class.text-accent]="filter === opt.value"
          [class.border-border]="filter !== opt.value"
          [class.text-muted]="filter !== opt.value"
        >
          {{ opt.label }}
        </button>
      }
    </div>

    @if (creating) {
      <div class="mb-5 rounded-card border border-border bg-raised p-4">
        <app-future-task-fields
          [title]="draftTitle"
          [notes]="draftNotes"
          [dueDate]="draftDueDate"
          [dueTime]="draftDueTime"
          [category]="draftCategory"
          [priority]="draftPriority"
          (titleChange)="draftTitle = $event"
          (notesChange)="draftNotes = $event"
          (dueDateChange)="draftDueDate = $event"
          (dueTimeChange)="draftDueTime = $event"
          (categoryChange)="draftCategory = $event"
          (priorityChange)="draftPriority = $event"
        ></app-future-task-fields>

        <div class="mt-3">
          <label class="mb-1.5 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Reminders (optional)</label>
          @for (r of draftReminders; track $index) {
            <div class="mb-1.5 flex items-center gap-2 text-[12px] text-muted">
              <span>{{ formatOffset(r.offsetMinutes) }} before · {{ r.channels.join(' + ') }}</span>
              <button type="button" (click)="removeDraftReminder($index)" class="text-danger">Remove</button>
            </div>
          }
          <div class="flex flex-wrap items-center gap-2">
            <input
              type="number"
              min="0"
              class="w-24 rounded-lg border border-border bg-surface px-2 py-1.5 text-[12px] text-text outline-none focus:border-accent"
              placeholder="minutes before"
              [(ngModel)]="newReminderOffset"
              name="newReminderOffset"
            />
            <label class="flex items-center gap-1 text-[12px] text-muted">
              <input type="checkbox" [(ngModel)]="newReminderInApp" name="newReminderInApp" /> In-app
            </label>
            <label class="flex items-center gap-1 text-[12px] text-muted">
              <input type="checkbox" [(ngModel)]="newReminderEmail" name="newReminderEmail" /> Email
            </label>
            <button type="button" (click)="addDraftReminder()" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted hover:text-text">
              + Add reminder
            </button>
          </div>
        </div>

        @if (error()) {
          <div class="mt-3 text-[12px] font-medium text-danger">{{ error() }}</div>
        }

        <div class="mt-3 flex justify-end">
          <button type="button" (click)="create()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Create task</button>
        </div>
      </div>
    }

    @if (loading()) {
      <div class="py-8 text-center text-[13px] text-muted">Loading tasks…</div>
    } @else if (tasks().length === 0) {
      <div class="rounded-card border border-border bg-raised p-6 text-center text-[13px] text-muted">Nothing upcoming.</div>
    } @else {
      <ul class="divide-y divide-border rounded-card border border-border bg-raised">
        @for (t of tasks(); track t.id) {
          @if (editingId === t.id) {
            <li class="bg-raised2/40 p-4">
              <app-future-task-fields
                [title]="editTitle"
                [notes]="editNotes"
                [dueDate]="editDueDate"
                [dueTime]="editDueTime"
                [category]="editCategory"
                [priority]="editPriority"
                (titleChange)="editTitle = $event"
                (notesChange)="editNotes = $event"
                (dueDateChange)="editDueDate = $event"
                (dueTimeChange)="editDueTime = $event"
                (categoryChange)="editCategory = $event"
                (priorityChange)="editPriority = $event"
              ></app-future-task-fields>

              <div class="mt-3">
                <label class="mb-1.5 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Reminders</label>
                @for (r of t.reminders; track r.id) {
                  <div class="mb-1.5 flex items-center gap-2 text-[12px] text-muted">
                    <span>{{ formatOffset(r.offsetMinutes) }} before · {{ r.channels }} · {{ r.status }}</span>
                    <button type="button" (click)="removeReminder(t, r.id)" class="text-danger">Remove</button>
                  </div>
                }
                <div class="flex flex-wrap items-center gap-2">
                  <input
                    type="number"
                    min="0"
                    class="w-24 rounded-lg border border-border bg-surface px-2 py-1.5 text-[12px] text-text outline-none focus:border-accent"
                    placeholder="minutes before"
                    [(ngModel)]="newReminderOffset"
                    name="editNewReminderOffset"
                  />
                  <label class="flex items-center gap-1 text-[12px] text-muted">
                    <input type="checkbox" [(ngModel)]="newReminderInApp" name="editNewReminderInApp" /> In-app
                  </label>
                  <label class="flex items-center gap-1 text-[12px] text-muted">
                    <input type="checkbox" [(ngModel)]="newReminderEmail" name="editNewReminderEmail" /> Email
                  </label>
                  <button type="button" (click)="addReminder(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted hover:text-text">
                    + Add reminder
                  </button>
                </div>
              </div>

              @if (error()) {
                <div class="mt-3 text-[12px] font-medium text-danger">{{ error() }}</div>
              }

              <div class="mt-3 flex justify-end gap-2">
                <button type="button" (click)="editingId = null" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
                <button type="button" (click)="saveEdit(t)" class="rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white">Save</button>
              </div>
            </li>
          } @else {
            <li class="flex flex-wrap items-center gap-3 px-4 py-3">
              <span class="h-3.5 w-[3px] flex-none self-stretch rounded" [style.background]="priorityColor(t.priority)"></span>
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium" [class.line-through]="t.status === 'Done' || t.status === 'Cancelled'" [class.text-muted]="t.status === 'Done' || t.status === 'Cancelled'">
                  {{ t.title }}
                </b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  <span>{{ t.dueDate }}{{ t.dueTime ? ' · ' + t.dueTime.slice(0, 5) : '' }}</span>
                  @if (t.category) {
                    <span>· {{ t.category }}</span>
                  }
                  @if (t.reminders.length) {
                    <span>· {{ t.reminders.length }} reminder{{ t.reminders.length > 1 ? 's' : '' }}</span>
                  }
                </div>
              </div>
              <span class="rounded-full px-2 py-0.5 text-[10px] font-bold" [style.color]="statusColor(t.status)" [style.background]="statusColor(t.status) + '26'">
                {{ t.status }}
              </span>
              @if (t.status === 'Pending' || t.status === 'Deferred') {
                <button type="button" (click)="setStatus(t, 'Done')" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Done</button>
                <button type="button" (click)="deferPrompt(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Defer</button>
                <button type="button" (click)="setStatus(t, 'Cancelled')" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Cancel</button>
              } @else {
                <button type="button" (click)="setStatus(t, 'Pending')" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Reopen</button>
              }
              <button type="button" (click)="startEdit(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">Edit</button>
              <button type="button" (click)="remove(t)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">Delete</button>
            </li>
          }
        }
      </ul>
    }
  `
})
export class UpcomingPageComponent implements OnInit {
  private readonly api = inject(FutureTasksApi);

  protected readonly tasks = signal<FutureTask[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly statusFilters: { label: string; value: FutureTaskStatusName | null }[] = [
    { label: 'All', value: null },
    { label: 'Pending', value: 'Pending' },
    { label: 'Deferred', value: 'Deferred' },
    { label: 'Done', value: 'Done' },
    { label: 'Cancelled', value: 'Cancelled' }
  ];
  protected filter: FutureTaskStatusName | null = null;

  protected creating = false;
  protected draftTitle = '';
  protected draftNotes = '';
  protected draftDueDate = '';
  protected draftDueTime = '';
  protected draftCategory = '';
  protected draftPriority: TaskPriorityName = 'Normal';
  protected draftReminders: DraftReminder[] = [];

  protected editingId: string | null = null;
  protected editTitle = '';
  protected editNotes = '';
  protected editDueDate = '';
  protected editDueTime = '';
  protected editCategory = '';
  protected editPriority: TaskPriorityName = 'Normal';

  protected newReminderOffset = 30;
  protected newReminderInApp = true;
  protected newReminderEmail = false;

  ngOnInit(): void {
    this.load();
  }

  protected setFilter(value: FutureTaskStatusName | null): void {
    this.filter = value;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.list(this.filter ?? undefined).subscribe({
      next: (tasks) => {
        this.tasks.set(tasks);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  protected priorityColor(priority: string): string {
    return PRIORITY_COLOR[priority] ?? '#94a3b8';
  }
  protected statusColor(status: string): string {
    return STATUS_COLOR[status] ?? '#94a3b8';
  }
  protected formatOffset(minutes: number): string {
    if (minutes === 0) return 'At due time';
    if (minutes < 60) return `${minutes}m`;
    if (minutes < 1440) return `${Math.round(minutes / 60)}h`;
    return `${Math.round(minutes / 1440)}d`;
  }

  private buildDraftChannels(): NotificationChannelName[] {
    const channels: NotificationChannelName[] = [];
    if (this.newReminderInApp) channels.push('InApp');
    if (this.newReminderEmail) channels.push('Email');
    return channels.length ? channels : ['InApp'];
  }

  protected addDraftReminder(): void {
    this.draftReminders = [...this.draftReminders, { offsetMinutes: Number(this.newReminderOffset) || 0, channels: this.buildDraftChannels() }];
  }

  protected removeDraftReminder(index: number): void {
    this.draftReminders = this.draftReminders.filter((_, i) => i !== index);
  }

  protected create(): void {
    const title = this.draftTitle.trim();
    if (!title || !this.draftDueDate) {
      this.error.set('Title and due date are required.');
      return;
    }

    const request: FutureTaskRequest = {
      title,
      notes: this.draftNotes.trim() || null,
      dueDate: this.draftDueDate,
      dueTime: toApiTime(this.draftDueTime),
      category: this.draftCategory.trim() || null,
      priority: this.draftPriority,
      reminders: this.draftReminders.map((r): ReminderRequest => ({ offsetMinutes: r.offsetMinutes, channels: r.channels }))
    };

    this.api.create(request).subscribe({
      next: (created) => {
        this.tasks.update((list) => [...list, created].sort((a, b) => a.dueDate.localeCompare(b.dueDate)));
        this.draftTitle = '';
        this.draftNotes = '';
        this.draftDueDate = '';
        this.draftDueTime = '';
        this.draftCategory = '';
        this.draftPriority = 'Normal';
        this.draftReminders = [];
        this.creating = false;
        this.error.set(null);
      },
      error: () => this.error.set('Could not create task.')
    });
  }

  protected startEdit(t: FutureTask): void {
    this.editingId = t.id;
    this.creating = false;
    this.editTitle = t.title;
    this.editNotes = t.notes ?? '';
    this.editDueDate = t.dueDate;
    this.editDueTime = toTimeInput(t.dueTime);
    this.editCategory = t.category ?? '';
    this.editPriority = t.priority;
    this.error.set(null);
  }

  protected saveEdit(t: FutureTask): void {
    const title = this.editTitle.trim();
    if (!title || !this.editDueDate) {
      this.error.set('Title and due date are required.');
      return;
    }

    const request: FutureTaskRequest = {
      title,
      notes: this.editNotes.trim() || null,
      dueDate: this.editDueDate,
      dueTime: toApiTime(this.editDueTime),
      category: this.editCategory.trim() || null,
      priority: this.editPriority,
      reminders: null
    };

    this.api.update(t.id, request).subscribe({
      next: (updated) => {
        this.tasks.update((list) => list.map((x) => (x.id === t.id ? updated : x)).sort((a, b) => a.dueDate.localeCompare(b.dueDate)));
        this.editingId = null;
        this.error.set(null);
      },
      error: () => this.error.set('Could not save task.')
    });
  }

  protected addReminder(t: FutureTask): void {
    const request: ReminderRequest = { offsetMinutes: Number(this.newReminderOffset) || 0, channels: this.buildDraftChannels() };
    this.api.createReminder(t.id, request).subscribe({
      next: (reminder) => this.tasks.update((list) => list.map((x) => (x.id === t.id ? { ...x, reminders: [...x.reminders, reminder] } : x))),
      error: () => this.error.set('Could not add reminder.')
    });
  }

  protected removeReminder(t: FutureTask, reminderId: string): void {
    this.api.removeReminder(t.id, reminderId).subscribe({
      next: () => this.tasks.update((list) => list.map((x) => (x.id === t.id ? { ...x, reminders: x.reminders.filter((r) => r.id !== reminderId) } : x))),
      error: () => this.error.set('Could not remove reminder.')
    });
  }

  protected setStatus(t: FutureTask, status: FutureTaskStatusName): void {
    this.api.setStatus(t.id, status).subscribe({
      next: (updated) => this.tasks.update((list) => list.map((x) => (x.id === t.id ? updated : x))),
      error: () => this.error.set('Could not update status.')
    });
  }

  protected deferPrompt(t: FutureTask): void {
    const input = window.prompt('Defer to new due date (YYYY-MM-DD):', t.dueDate);
    if (!input) return;
    this.api.defer(t.id, input).subscribe({
      next: (updated) => this.tasks.update((list) => list.map((x) => (x.id === t.id ? updated : x)).sort((a, b) => a.dueDate.localeCompare(b.dueDate))),
      error: () => this.error.set('Could not defer task.')
    });
  }

  protected remove(t: FutureTask): void {
    this.api.remove(t.id).subscribe({
      next: () => this.tasks.update((list) => list.filter((x) => x.id !== t.id)),
      error: () => this.error.set('Could not delete task.')
    });
  }
}
