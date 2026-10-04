import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { TasksStore } from '../../core/state/tasks.store';
import { EmptyStateComponent } from '../../shared/ui/empty-state.component';
import { TaskRowComponent } from './task-row.component';

// Standalone backlog page — plan section 5.5b / 3b. This component and its
// TasksStore must stay a closed loop: no other feature imports this
// component, and this component imports nothing from core/state other than
// TasksStore (verified by grep — see the delivery note for this task).
// `providers: [TasksStore]` scopes the store's lifetime to this routed
// component: it's created when you navigate to /tasks and destroyed the
// moment you navigate away, so the backlog only ever loads while you're
// actually looking at it.
@Component({
  selector: 'app-tasks-page',
  imports: [FormsModule, TaskRowComponent, EmptyStateComponent],
  providers: [TasksStore],
  template: `
    <div class="mb-4 flex flex-wrap items-center gap-3">
      <h1 class="text-[22px] font-bold tracking-tight text-text">Tasks</h1>
      <span class="rounded-full border border-border bg-raised px-2.5 py-1 text-[11.5px] font-semibold text-muted">
        {{ store.openCount() }} open
      </span>
      <p class="mt-[-6px] w-full text-[13px] text-muted">
        Your own backlog. Nothing here appears on Today, Checklists, Timetable or Upcoming — this list
        exists only in this section, and you add, edit or remove entries only while you're looking at it.
      </p>
    </div>

    <div class="grid items-start gap-5 lg:grid-cols-[1.3fr_1fr]">
      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Backlog</h3>
          <div class="ml-auto">
            <span class="rounded-full border border-border bg-raised px-2.5 py-1 text-[11px] font-semibold text-muted">
              {{ store.openCount() }} open · {{ store.doneCount() }} done
            </span>
          </div>
        </div>

        <div class="flex gap-2.5 border-b border-border px-4 py-3">
          <input
            class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
            placeholder="Add a task and press Enter…"
            [(ngModel)]="draftTitle"
            (keydown.enter)="addTask()"
          />
          <button
            type="button"
            (click)="addTask()"
            class="flex-none rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
          >
            Add
          </button>
        </div>

        @if (store.loading() && store.tasks().length === 0) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">Loading tasks…</div>
        } @else if (store.tasks().length === 0) {
          <app-empty-state
            message="No tasks yet — add your first one above."
          ></app-empty-state>
        } @else {
          <div>
            @for (task of store.tasks(); track task.id) {
              <app-task-row
                [task]="task"
                (toggle)="store.toggle(task.id)"
                (remove)="store.remove(task.id)"
                (save)="store.update(task.id, $event)"
              ></app-task-row>
            }
          </div>
        }

        @if (store.error()) {
          <div class="border-t border-border px-4 py-2.5 text-[12px] font-medium text-danger">
            {{ store.error() }}
          </div>
        }
      </div>

      <div class="rounded-card border border-border bg-raised p-5 shadow-[var(--shadow)]">
        <h4 class="mb-3 text-[11.5px] font-bold uppercase tracking-wider text-muted">
          Why this section is separate
        </h4>
        <div class="space-y-3 text-[13px] leading-relaxed text-text">
          <p>
            <b>No date. No recurrence. No reminder.</b> This is the one list in the app with zero
            scheduling logic attached — nothing here is ever pulled into the
            <code class="text-[12px]">/today</code> aggregate or the notification pipeline.
          </p>
          <p>
            It only loads when you open <b>Tasks</b>, and every add / edit / delete happens right here —
            nowhere else in the app reads or writes this list.
          </p>
          <p>
            If something on this list turns out to need a real date and a reminder, that's your cue to
            promote it: recreate it as a <b>Checklist item</b> (if it repeats) or an
            <b>Upcoming</b> task (if it's a one-off with a deadline).
          </p>
        </div>
      </div>
    </div>
  `
})
export class TasksPageComponent implements OnInit {
  protected readonly store = inject(TasksStore);
  protected draftTitle = '';

  ngOnInit(): void {
    this.store.load();
  }

  protected addTask(): void {
    const title = this.draftTitle.trim();
    if (!title) return;
    this.store.add(title);
    this.draftTitle = '';
  }
}
