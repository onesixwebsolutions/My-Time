import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { SimpleTask, TaskPriority } from '../../core/api/tasks.api';

// Extracted as its own small component (rather than kept inline in an @for
// loop in tasks-page) because the row has several independent pieces of
// interaction state (toggle, remove, and now inline edit) and
// priority-driven styling — pulling it out keeps tasks-page.component.ts
// focused on list orchestration (quick-add, empty state, store wiring) and
// makes the row itself trivially reusable/testable in isolation, matching
// the plan's suggested tasks-page / task-row split (section 6.2).
const PRIORITY_COLOR: Record<string, string> = {
  Low: '#94a3b8',
  Normal: '#94a3b8',
  High: '#f59e0b',
  Critical: '#ef4444'
};

const PRIORITY_LABEL: Record<string, string> = {
  Low: 'Low',
  Normal: 'Normal',
  High: 'High',
  Critical: 'Critical'
};

@Component({
  selector: 'app-task-row',
  imports: [FormsModule],
  template: `
    @if (editing) {
      <div class="space-y-2 border-b border-border bg-raised2/40 px-4 py-3 last:border-b-0">
        <div class="grid grid-cols-[1fr_110px] gap-2">
          <input
            class="rounded-lg border border-border bg-surface px-3 py-1.5 text-[13px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftTitle"
            name="editTitle"
          />
          <select
            class="rounded-lg border border-border bg-surface px-2 py-1.5 text-[12px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftPriority"
          >
            <option value="Low">Low</option>
            <option value="Normal">Normal</option>
            <option value="High">High</option>
            <option value="Critical">Critical</option>
          </select>
        </div>
        <textarea
          class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-1.5 text-[12px] text-text outline-none focus:border-accent"
          rows="2"
          placeholder="Notes (optional)"
          [(ngModel)]="draftNotes"
        ></textarea>
        <div class="flex justify-end gap-2">
          <button type="button" (click)="cancelEdit()" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted">Cancel</button>
          <button type="button" (click)="confirmEdit()" class="rounded-lg bg-accent px-2.5 py-1 text-[11.5px] font-semibold text-white">Save</button>
        </div>
      </div>
    } @else {
      <div
        class="group flex items-start gap-3 border-b border-border px-4 py-2.5 transition-colors last:border-b-0 hover:bg-raised2"
      >
        <span class="mt-1 h-3.5 w-[3px] flex-none rounded" [style.background]="priorityColor"></span>

        <button
          type="button"
          (click)="toggle.emit()"
          class="mt-0.5 grid h-[17px] w-[17px] flex-none place-items-center rounded-[5px] border-[1.5px] border-border transition-colors"
          [class.bg-success]="task.status === 'Done'"
          [class.border-success]="task.status === 'Done'"
          [attr.aria-label]="task.status === 'Done' ? 'Mark open' : 'Mark done'"
        >
          @if (task.status === 'Done') {
            <svg viewBox="0 0 24 24" fill="none" class="h-[11px] w-[11px] stroke-white" stroke-width="3.2">
              <path d="m5 13 4 4L19 7" />
            </svg>
          }
        </button>

        <div class="min-w-0 flex-1">
          <b
            class="block text-[13.2px] font-medium leading-snug"
            [class.line-through]="task.status === 'Done'"
            [class.text-muted]="task.status === 'Done'"
          >
            {{ task.title }}
          </b>
          <div class="mt-1 flex items-center gap-2">
            <span class="text-[10.5px] font-medium text-muted">{{ priorityLabel }}</span>
            @if (task.notes) {
              <span class="truncate text-[10.5px] text-muted">· {{ task.notes }}</span>
            }
          </div>
        </div>

        <button
          type="button"
          (click)="startEdit()"
          class="grid h-[26px] w-[26px] flex-none place-items-center rounded-[9px] text-muted opacity-0 transition-opacity hover:bg-raised2 hover:text-text group-hover:opacity-100"
          aria-label="Edit task"
        >
          <svg viewBox="0 0 24 24" fill="none" class="h-[13px] w-[13px] stroke-current" stroke-width="2">
            <path d="M12 20h9M16.5 3.5a2.121 2.121 0 0 1 3 3L7 19l-4 1 1-4L16.5 3.5Z" />
          </svg>
        </button>

        <button
          type="button"
          (click)="remove.emit()"
          class="grid h-[26px] w-[26px] flex-none place-items-center rounded-[9px] text-muted opacity-0 transition-opacity hover:bg-raised2 hover:text-danger group-hover:opacity-100"
          aria-label="Remove task"
        >
          <svg viewBox="0 0 24 24" fill="none" class="h-[13px] w-[13px] stroke-current" stroke-width="2.3">
            <path d="M18 6 6 18M6 6l12 12" />
          </svg>
        </button>
      </div>
    }
  `
})
export class TaskRowComponent {
  @Input({ required: true }) task!: SimpleTask;
  @Output() toggle = new EventEmitter<void>();
  @Output() remove = new EventEmitter<void>();
  @Output() save = new EventEmitter<{ title: string; notes: string | null; priority: TaskPriority }>();

  protected editing = false;
  protected draftTitle = '';
  protected draftNotes = '';
  protected draftPriority: TaskPriority = 'Normal';

  get priorityColor(): string {
    return PRIORITY_COLOR[this.task.priority] ?? '#94a3b8';
  }

  get priorityLabel(): string {
    return PRIORITY_LABEL[this.task.priority] ?? 'Normal';
  }

  protected startEdit(): void {
    this.draftTitle = this.task.title;
    this.draftNotes = this.task.notes ?? '';
    this.draftPriority = this.task.priority;
    this.editing = true;
  }

  protected cancelEdit(): void {
    this.editing = false;
  }

  protected confirmEdit(): void {
    const title = this.draftTitle.trim();
    if (!title) return;
    this.save.emit({ title, notes: this.draftNotes.trim() || null, priority: this.draftPriority });
    this.editing = false;
  }
}
