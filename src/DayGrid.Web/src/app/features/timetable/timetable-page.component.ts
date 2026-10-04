import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { TemplateRequest, TimetableApi, TimetableTemplate } from '../../core/api/timetable.api';

function toApiTime(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

// Full CRUD for the template list: create, delete, set default. Editing a
// template's own fields and its blocks lives on the detail page at
// /timetable/:id.
@Component({
  selector: 'app-timetable-page',
  standalone: true,
  imports: [FormsModule, RouterLink],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Timetable</h1>
        <p class="mt-1 text-[13px] text-muted">Reusable shapes of a day, built from hour/minute blocks.</p>
      </div>
      <button
        type="button"
        (click)="creating = !creating"
        class="ml-auto rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
      >
        {{ creating ? 'Cancel' : '+ New template' }}
      </button>
    </div>

    @if (creating) {
      <div class="mb-5 space-y-2.5 rounded-card border border-border bg-raised p-4">
        <input
          class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
          placeholder="Template name"
          [(ngModel)]="draftName"
          name="draftName"
        />
        <textarea
          class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          rows="2"
          placeholder="Description (optional)"
          [(ngModel)]="draftDescription"
          name="draftDescription"
        ></textarea>
        <div class="flex items-center gap-2 text-[12.5px] text-muted">
          Day runs
          <input type="time" class="rounded-lg border border-border bg-surface px-2.5 py-1.5 text-text outline-none focus:border-accent" [(ngModel)]="draftDayStart" name="draftDayStart" />
          to
          <input type="time" class="rounded-lg border border-border bg-surface px-2.5 py-1.5 text-text outline-none focus:border-accent" [(ngModel)]="draftDayEnd" name="draftDayEnd" />
          slot
          <input
            type="number"
            min="5"
            class="w-16 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
            [(ngModel)]="draftSlotMinutes"
            name="draftSlotMinutes"
          />
          min
        </div>
        <div class="flex justify-end">
          <button type="button" (click)="create()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">Create template</button>
        </div>
      </div>
    }

    @if (loading()) {
      <div class="py-8 text-center text-[13px] text-muted">Loading templates…</div>
    } @else if (error()) {
      <div class="rounded-card border border-border bg-raised p-4 text-[13px] text-danger">{{ error() }}</div>
    } @else if (templates().length === 0) {
      <div class="rounded-card border border-border bg-raised p-6 text-center text-[13px] text-muted">No templates yet.</div>
    } @else {
      <ul class="divide-y divide-border rounded-card border border-border bg-raised">
        @for (tpl of templates(); track tpl.id) {
          <li class="flex items-center gap-3 px-4 py-3">
            <a [routerLink]="['/timetable', tpl.id]" class="text-[13.5px] font-medium text-text hover:text-accent">{{ tpl.name }}</a>
            @if (tpl.isDefault) {
              <span class="rounded-full px-2 py-0.5 text-[10px] font-bold text-success" style="background:rgba(34,197,94,.15)">Default</span>
            }
            <span class="ml-auto text-[11.5px] text-muted">{{ tpl.dayStart.slice(0, 5) }} – {{ tpl.dayEnd.slice(0, 5) }}</span>
            @if (!tpl.isDefault) {
              <button type="button" (click)="setDefault(tpl)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">
                Set default
              </button>
            }
            <button type="button" (click)="remove(tpl)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">
              Delete
            </button>
          </li>
        }
      </ul>
    }
  `
})
export class TimetablePageComponent implements OnInit {
  private readonly api = inject(TimetableApi);

  protected readonly templates = signal<TimetableTemplate[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected creating = false;
  protected draftName = '';
  protected draftDescription = '';
  protected draftDayStart = '06:00';
  protected draftDayEnd = '23:00';
  protected draftSlotMinutes = 30;

  ngOnInit(): void {
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.listTemplates().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load templates.');
        this.loading.set(false);
      }
    });
  }

  protected create(): void {
    const name = this.draftName.trim();
    if (!name) return;
    const request: TemplateRequest = {
      name,
      description: this.draftDescription.trim() || null,
      dayStart: toApiTime(this.draftDayStart),
      dayEnd: toApiTime(this.draftDayEnd),
      slotMinutes: Number(this.draftSlotMinutes) || 30
    };
    this.api.createTemplate(request).subscribe({
      next: (created) => {
        this.templates.update((list) => [...list, created]);
        this.draftName = '';
        this.draftDescription = '';
        this.draftDayStart = '06:00';
        this.draftDayEnd = '23:00';
        this.draftSlotMinutes = 30;
        this.creating = false;
      },
      error: () => this.error.set('Could not create template.')
    });
  }

  protected setDefault(tpl: TimetableTemplate): void {
    this.api.setDefaultTemplate(tpl.id).subscribe({
      next: () => this.templates.update((list) => list.map((t) => ({ ...t, isDefault: t.id === tpl.id }))),
      error: () => this.error.set('Could not set default template.')
    });
  }

  protected remove(tpl: TimetableTemplate): void {
    this.api.deleteTemplate(tpl.id).subscribe({
      next: () => this.templates.update((list) => list.filter((t) => t.id !== tpl.id)),
      error: () => this.error.set('Could not delete template.')
    });
  }
}
