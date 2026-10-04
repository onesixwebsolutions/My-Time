import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { TaskPriorityName } from '../../core/api/future-tasks.api';

// Shared title/notes/due-date/category/priority field group — used by both
// the create form and the inline edit form on the Upcoming page, so the two
// stay visually and behaviorally identical without duplicating markup.
@Component({
  selector: 'app-future-task-fields',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="grid grid-cols-[1fr_110px] gap-2.5">
      <input
        class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
        placeholder="Task title"
        [ngModel]="title"
        (ngModelChange)="titleChange.emit($event)"
        name="ftTitle"
      />
      <select
        class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
        [ngModel]="priority"
        (ngModelChange)="priorityChange.emit($event)"
        name="ftPriority"
      >
        <option value="Low">Low</option>
        <option value="Normal">Normal</option>
        <option value="High">High</option>
        <option value="Critical">Critical</option>
      </select>
    </div>

    <textarea
      class="mt-2.5 w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
      rows="2"
      placeholder="Notes (optional)"
      [ngModel]="notes"
      (ngModelChange)="notesChange.emit($event)"
      name="ftNotes"
    ></textarea>

    <div class="mt-2.5 grid grid-cols-[140px_110px_1fr] gap-2.5">
      <input
        type="date"
        class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
        [ngModel]="dueDate"
        (ngModelChange)="dueDateChange.emit($event)"
        name="ftDueDate"
      />
      <input
        type="time"
        class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
        [ngModel]="dueTime"
        (ngModelChange)="dueTimeChange.emit($event)"
        name="ftDueTime"
      />
      <input
        class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
        placeholder="Category (optional)"
        [ngModel]="category"
        (ngModelChange)="categoryChange.emit($event)"
        name="ftCategory"
      />
    </div>
  `
})
export class FutureTaskFieldsComponent {
  @Input() title = '';
  @Input() notes = '';
  @Input() dueDate = '';
  @Input() dueTime = '';
  @Input() category = '';
  @Input() priority: TaskPriorityName = 'Normal';

  @Output() titleChange = new EventEmitter<string>();
  @Output() notesChange = new EventEmitter<string>();
  @Output() dueDateChange = new EventEmitter<string>();
  @Output() dueTimeChange = new EventEmitter<string>();
  @Output() categoryChange = new EventEmitter<string>();
  @Output() priorityChange = new EventEmitter<TaskPriorityName>();
}
