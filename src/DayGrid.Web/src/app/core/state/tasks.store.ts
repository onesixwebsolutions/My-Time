import { Injectable, computed, inject, signal } from '@angular/core';
import { finalize } from 'rxjs';

import { SimpleTask, TasksApi } from '../api/tasks.api';

// IMPORTANT — scoping note (do not change without re-reading plan section 5.5b / 6.2):
//
// This store is intentionally injected ONLY at the component level by
// TasksPageComponent (`providers: [TasksStore]`), never registered with
// `providedIn: 'root'`. That means:
//   - it does not exist, and holds no data, until the /tasks route is actually
//     opened;
//   - it is torn down (and its data discarded) the moment you navigate away;
//   - no other feature can inject it and accidentally leak backlog items
//     into Today, Checklists, Timetable or Upcoming.
// This mirrors the backend's TasksService, which has no dependency on
// DayPlanBuilder or the reminder pipeline — the isolation is deliberate on
// both sides of the wire.
@Injectable()
export class TasksStore {
  private readonly api = inject(TasksApi);

  readonly tasks = signal<SimpleTask[]>([]);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);

  readonly openCount = computed(() => this.tasks().filter((t) => t.status === 'Open').length);
  readonly doneCount = computed(() => this.tasks().filter((t) => t.status === 'Done').length);

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .list()
      .pipe(finalize(() => this.loading.set(false)))
      .subscribe({
        next: (tasks) => this.tasks.set(tasks),
        error: () => this.error.set('Could not load tasks.')
      });
  }

  add(title: string): void {
    const trimmed = title.trim();
    if (!trimmed) return;

    const tempId = `temp-${crypto.randomUUID()}`;
    const optimistic: SimpleTask = {
      id: tempId,
      title: trimmed,
      notes: null,
      priority: 'Normal',
      status: 'Open',
      sortOrder: 0,
      completedAt: null,
      createdAt: new Date().toISOString(),
      updatedAt: new Date().toISOString()
    };
    this.tasks.update((tasks) => [optimistic, ...tasks]);

    this.api.create({ title: trimmed }).subscribe({
      next: (created) => {
        this.tasks.update((tasks) => tasks.map((t) => (t.id === tempId ? created : t)));
      },
      error: () => {
        this.tasks.update((tasks) => tasks.filter((t) => t.id !== tempId));
        this.error.set('Could not add task.');
      }
    });
  }

  update(id: string, changes: { title: string; notes: string | null; priority: SimpleTask['priority'] }): void {
    const previous = this.tasks();
    const target = previous.find((t) => t.id === id);
    if (!target) return;

    this.tasks.update((tasks) => tasks.map((t) => (t.id === id ? { ...t, ...changes } : t)));

    this.api.update(id, changes).subscribe({
      next: (saved) => this.tasks.update((tasks) => tasks.map((t) => (t.id === id ? saved : t))),
      error: () => {
        this.tasks.set(previous);
        this.error.set('Could not save task.');
      }
    });
  }

  toggle(id: string): void {
    const previous = this.tasks();
    const target = previous.find((t) => t.id === id);
    if (!target) return;
    const nextStatus = target.status === 'Open' ? 'Done' : 'Open';

    this.tasks.update((tasks) =>
      tasks.map((t) =>
        t.id === id
          ? { ...t, status: nextStatus, completedAt: nextStatus === 'Done' ? new Date().toISOString() : null }
          : t
      )
    );

    this.api.updateStatus(id, nextStatus).subscribe({
      error: () => {
        this.tasks.set(previous);
        this.error.set('Could not update task.');
      }
    });
  }

  remove(id: string): void {
    const previous = this.tasks();
    this.tasks.update((tasks) => tasks.filter((t) => t.id !== id));

    this.api.remove(id).subscribe({
      error: () => {
        this.tasks.set(previous);
        this.error.set('Could not remove task.');
      }
    });
  }
}
