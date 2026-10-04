import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  AssignmentRequest,
  AssignmentScopeName,
  DayOfWeekName,
  TimetableApi,
  TimetableAssignment,
  TimetableTemplate
} from '../../core/api/timetable.api';

const DAYS: DayOfWeekName[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

// Full CRUD (create + delete — assignments are simple enough that "edit"
// is delete-and-recreate) for which template applies on which day.
@Component({
  selector: 'app-assignments-page',
  imports: [FormsModule],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Assignments</h1>
        <p class="mt-1 text-[13px] text-muted">Which template applies on which day.</p>
      </div>
      <button
        type="button"
        (click)="creating = !creating"
        class="ml-auto rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
      >
        {{ creating ? 'Cancel' : '+ New assignment' }}
      </button>
    </div>

    @if (creating) {
      <div class="mb-5 space-y-2.5 rounded-card border border-border bg-raised p-4">
        <div class="grid grid-cols-2 gap-2.5">
          <select
            class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftTemplateId"
            name="draftTemplateId"
          >
            <option value="" disabled>Choose template…</option>
            @for (tpl of templates(); track tpl.id) {
              <option [value]="tpl.id">{{ tpl.name }}</option>
            }
          </select>
          <select
            class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftScope"
            name="draftScope"
          >
            <option value="Weekday">Weekday</option>
            <option value="SpecificDate">Specific date</option>
            <option value="DateRange">Date range</option>
          </select>
        </div>

        @if (draftScope === 'Weekday') {
          <select
            class="w-full rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftDayOfWeek"
            name="draftDayOfWeek"
          >
            @for (day of days; track day) {
              <option [value]="day">{{ day }}</option>
            }
          </select>
        }
        @if (draftScope === 'SpecificDate') {
          <input
            type="date"
            class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="draftDateFrom"
            name="draftDateFrom"
          />
        }
        @if (draftScope === 'DateRange') {
          <div class="flex items-center gap-2">
            <input
              type="date"
              class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
              [(ngModel)]="draftDateFrom"
              name="draftDateFrom"
            />
            <span class="text-[12px] text-muted">to</span>
            <input
              type="date"
              class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
              [(ngModel)]="draftDateTo"
              name="draftDateTo"
            />
          </div>
        }

        <div class="flex items-center gap-2 text-[12.5px] text-muted">
          Priority (higher wins ties)
          <input
            type="number"
            class="w-16 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
            [(ngModel)]="draftPriority"
            name="draftPriority"
          />
        </div>

        @if (error()) {
          <div class="text-[12px] font-medium text-danger">{{ error() }}</div>
        }

        <div class="flex justify-end">
          <button type="button" (click)="create()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Create assignment</button>
        </div>
      </div>
    }

    @if (assignments().length === 0) {
      <div class="rounded-card border border-border bg-raised p-6 text-center text-[13px] text-muted">No assignments yet.</div>
    } @else {
      <ul class="divide-y divide-border rounded-card border border-border bg-raised">
        @for (a of assignments(); track a.id) {
          <li class="flex items-center gap-3 px-4 py-3 text-[13px]">
            <span class="font-medium text-text">{{ templateName(a.templateId) }}</span>
            <span class="text-muted">·</span>
            <span class="text-muted">{{ scopeLabel(a) }}</span>
            <span class="ml-auto text-[11px] text-muted">priority {{ a.priority }}</span>
            <button type="button" (click)="remove(a)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">
              Delete
            </button>
          </li>
        }
      </ul>
    }
  `
})
export class AssignmentsPageComponent implements OnInit {
  private readonly api = inject(TimetableApi);

  protected readonly assignments = signal<TimetableAssignment[]>([]);
  protected readonly templates = signal<TimetableTemplate[]>([]);
  protected readonly error = signal<string | null>(null);

  protected readonly days = DAYS;
  protected creating = false;
  protected draftTemplateId = '';
  protected draftScope: AssignmentScopeName = 'Weekday';
  protected draftDayOfWeek: DayOfWeekName = 'Monday';
  protected draftDateFrom = '';
  protected draftDateTo = '';
  protected draftPriority = 0;

  ngOnInit(): void {
    this.api.listAssignments().subscribe({ next: (a) => this.assignments.set(a), error: () => void 0 });
    this.api.listTemplates().subscribe({ next: (t) => this.templates.set(t), error: () => void 0 });
  }

  protected templateName(id: string): string {
    return this.templates().find((t) => t.id === id)?.name ?? 'Unknown template';
  }

  protected scopeLabel(a: TimetableAssignment): string {
    switch (a.scope) {
      case 'Weekday':
        return a.dayOfWeek ?? 'Weekday';
      case 'SpecificDate':
        return a.dateFrom ?? 'Specific date';
      case 'DateRange':
        return `${a.dateFrom ?? '?'} → ${a.dateTo ?? '?'}`;
      default:
        return a.scope;
    }
  }

  protected create(): void {
    if (!this.draftTemplateId) {
      this.error.set('Choose a template first.');
      return;
    }
    if (this.draftScope === 'SpecificDate' && !this.draftDateFrom) {
      this.error.set('Choose a date.');
      return;
    }
    if (this.draftScope === 'DateRange' && (!this.draftDateFrom || !this.draftDateTo)) {
      this.error.set('Choose both dates.');
      return;
    }

    const request: AssignmentRequest = {
      templateId: this.draftTemplateId,
      scope: this.draftScope,
      dayOfWeek: this.draftScope === 'Weekday' ? this.draftDayOfWeek : null,
      dateFrom: this.draftScope !== 'Weekday' ? this.draftDateFrom : null,
      dateTo: this.draftScope === 'DateRange' ? this.draftDateTo : null,
      priority: Number(this.draftPriority) || 0
    };

    this.api.createAssignment(request).subscribe({
      next: (created) => {
        this.assignments.update((list) => [...list, created]);
        this.error.set(null);
        this.creating = false;
        this.draftDateFrom = '';
        this.draftDateTo = '';
        this.draftPriority = 0;
      },
      error: () => this.error.set('Could not create assignment.')
    });
  }

  protected remove(a: TimetableAssignment): void {
    this.api.deleteAssignment(a.id).subscribe({
      next: () => this.assignments.update((list) => list.filter((x) => x.id !== a.id)),
      error: () => this.error.set('Could not delete assignment.')
    });
  }
}
