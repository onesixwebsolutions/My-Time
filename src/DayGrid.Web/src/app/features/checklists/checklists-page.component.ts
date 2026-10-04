import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { Checklist, ChecklistsApi } from '../../core/api/checklists.api';

const DEFAULT_COLOR = '#6366f1';

// Full CRUD for the checklist list: create, inline edit, archive/unarchive,
// delete. Item-level CRUD (anchor + recurrence editor) lives on the detail
// page at /checklists/:id.
@Component({
  selector: 'app-checklists-page',
  imports: [FormsModule, RouterLink],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Checklists</h1>
        <p class="mt-1 text-[13px] text-muted">Organise what has to happen. Each item carries a recurrence rule and a time anchor.</p>
      </div>
      <button
        type="button"
        (click)="creating = !creating"
        class="ml-auto rounded-lg bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
      >
        {{ creating ? 'Cancel' : '+ New checklist' }}
      </button>
      <label class="ml-2 flex items-center gap-1.5 text-[12px] text-muted">
        <input type="checkbox" [(ngModel)]="includeArchived" name="includeArchived" (change)="load()" />
        Show archived
      </label>
    </div>

    @if (creating) {
      <div class="mb-5 space-y-2.5 rounded-card border border-border bg-raised p-4">
        <div class="grid grid-cols-[1fr_90px_90px] gap-2.5">
          <input
            class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
            placeholder="Checklist name"
            [(ngModel)]="draftName"
            name="draftName"
            (keydown.enter)="create()"
          />
          <input type="color" class="h-[38px] w-full rounded-lg border border-border bg-surface" [(ngModel)]="draftColor" name="draftColor" />
          <input
            class="rounded-lg border border-border bg-surface px-3 py-2 text-center text-[13px] text-text outline-none focus:border-accent"
            placeholder="icon"
            [(ngModel)]="draftIcon"
            name="draftIcon"
          />
        </div>
        <textarea
          class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          rows="2"
          placeholder="Description (optional)"
          [(ngModel)]="draftDescription"
          name="draftDescription"
        ></textarea>
        <div class="flex justify-end">
          <button type="button" (click)="create()" class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white">
            Create checklist
          </button>
        </div>
      </div>
    }

    @if (loading()) {
      <div class="py-8 text-center text-[13px] text-muted">Loading checklists…</div>
    } @else if (error()) {
      <div class="rounded-card border border-border bg-raised p-4 text-[13px] text-danger">{{ error() }}</div>
    } @else if (checklists().length === 0) {
      <div class="rounded-card border border-border bg-raised p-6 text-center text-[13px] text-muted">No checklists yet.</div>
    } @else {
      <ul class="divide-y divide-border rounded-card border border-border bg-raised">
        @for (cl of checklists(); track cl.id) {
          <li class="flex items-center gap-3 px-4 py-3">
            <span class="h-2.5 w-2.5 flex-none rounded-full" [style.background]="cl.color || '#6366f1'"></span>
            <a [routerLink]="['/checklists', cl.id]" class="text-[13.5px] font-medium text-text hover:text-accent">{{ cl.name }}</a>
            @if (cl.isArchived) {
              <span class="rounded-full bg-raised2 px-2 py-0.5 text-[10.5px] font-semibold text-muted">Archived</span>
            }
            @if (cl.itemCount !== undefined) {
              <span class="ml-auto text-[11.5px] text-muted">{{ cl.itemCount }} items</span>
            }
            <button
              type="button"
              (click)="toggleArchive(cl)"
              class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted transition-colors hover:text-text"
            >
              {{ cl.isArchived ? 'Unarchive' : 'Archive' }}
            </button>
            <button
              type="button"
              (click)="remove(cl)"
              class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger transition-colors hover:bg-raised2"
            >
              Delete
            </button>
          </li>
        }
      </ul>
    }
  `
})
export class ChecklistsPageComponent implements OnInit {
  private readonly api = inject(ChecklistsApi);

  protected readonly checklists = signal<Checklist[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected includeArchived = false;
  protected creating = false;
  protected draftName = '';
  protected draftDescription = '';
  protected draftColor = DEFAULT_COLOR;
  protected draftIcon = '';

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    this.loading.set(true);
    this.api.list(this.includeArchived).subscribe({
      next: (checklists) => {
        this.checklists.set(checklists);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load checklists.');
        this.loading.set(false);
      }
    });
  }

  protected create(): void {
    const name = this.draftName.trim();
    if (!name) return;
    this.api
      .create({
        name,
        description: this.draftDescription.trim() || null,
        color: this.draftColor,
        icon: this.draftIcon.trim() || null,
        sortOrder: this.checklists().length
      })
      .subscribe({
        next: (created) => {
          this.checklists.update((list) => [...list, created]);
          this.draftName = '';
          this.draftDescription = '';
          this.draftColor = DEFAULT_COLOR;
          this.draftIcon = '';
          this.creating = false;
        },
        error: () => this.error.set('Could not create checklist.')
      });
  }

  protected toggleArchive(cl: Checklist): void {
    this.api.archive(cl.id, !cl.isArchived).subscribe({
      next: (updated) => {
        if (!this.includeArchived && updated.isArchived) {
          this.checklists.update((list) => list.filter((c) => c.id !== cl.id));
        } else {
          this.checklists.update((list) => list.map((c) => (c.id === cl.id ? updated : c)));
        }
      },
      error: () => this.error.set('Could not update checklist.')
    });
  }

  protected remove(cl: Checklist): void {
    this.api.remove(cl.id).subscribe({
      next: () => this.checklists.update((list) => list.filter((c) => c.id !== cl.id)),
      error: () => this.error.set('Could not delete checklist.')
    });
  }
}
