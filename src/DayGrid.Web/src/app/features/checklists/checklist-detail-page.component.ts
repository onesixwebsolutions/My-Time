import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { Checklist, ChecklistItem, ChecklistWithItems, ChecklistsApi } from '../../core/api/checklists.api';
import { ChecklistItemFormComponent } from './checklist-item-form.component';

const PRIORITY_COLOR: Record<string, string> = { Low: '#94a3b8', Normal: '#94a3b8', High: '#f59e0b', Critical: '#ef4444' };

function anchorSummary(item: ChecklistItem): string {
  switch (item.anchorType) {
    case 'FixedTime':
      return item.anchorTime ? item.anchorTime.slice(0, 5) : 'Fixed time';
    case 'TimeWindow':
      return item.windowStart && item.windowEnd ? `${item.windowStart.slice(0, 5)}–${item.windowEnd.slice(0, 5)}` : 'Window';
    case 'LinkedToBlock':
      return 'Linked to block';
    default:
      return 'Anytime';
  }
}

function recurrenceSummary(item: ChecklistItem): string {
  const r = item.recurrence;
  switch (r.type) {
    case 'None':
      return 'One-off';
    case 'Daily':
      return r.interval > 1 ? `Every ${r.interval} days` : 'Every day';
    case 'Weekly':
      return r.daysOfWeek.length ? `Every ${r.daysOfWeek.map((d) => d.slice(0, 3)).join(', ')}` : 'Weekly';
    case 'MonthlyByDay':
      return `Monthly on day ${r.dayOfMonth ?? '?'}`;
    case 'MonthlyByWeekday':
      return r.nthWeekday ? `Monthly, ${r.nthWeekday.nth}th ${r.nthWeekday.dayOfWeek}` : 'Monthly';
    case 'EveryNDays':
      return `Every ${r.interval} days`;
    case 'Custom':
      return r.daysOfWeek.length ? `Every ${r.daysOfWeek.map((d) => d.slice(0, 3)).join(', ')}` : 'Custom';
    default:
      return '';
  }
}

// Full CRUD for a single checklist's header + its items. Item edit form is
// the reusable app-checklist-item-form (anchor + recurrence editor).
@Component({
  selector: 'app-checklist-detail-page',
  standalone: true,
  imports: [FormsModule, RouterLink, ChecklistItemFormComponent],
  template: `
    @if (checklist(); as cl) {
      <div class="mb-5 flex flex-wrap items-start gap-3">
        <a routerLink="/checklists" class="mt-1.5 text-[12.5px] font-semibold text-muted hover:text-text">← Checklists</a>
      </div>

      @if (!editingHeader) {
        <div class="mb-5 flex flex-wrap items-center gap-3">
          <span class="h-3 w-3 flex-none rounded-full" [style.background]="cl.color || '#6366f1'"></span>
          <h1 class="text-[22px] font-bold tracking-tight text-text">{{ cl.name }}</h1>
          @if (cl.isArchived) {
            <span class="rounded-full bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">Archived</span>
          }
          <button
            type="button"
            (click)="startEditHeader()"
            class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted transition-colors hover:text-text"
          >
            Edit
          </button>
          <button
            type="button"
            (click)="toggleArchive(cl)"
            class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted transition-colors hover:text-text"
          >
            {{ cl.isArchived ? 'Unarchive' : 'Archive' }}
          </button>
          <button
            type="button"
            (click)="deleteChecklist(cl)"
            class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-danger transition-colors hover:bg-raised2"
          >
            Delete
          </button>
        </div>
        @if (cl.description) {
          <p class="mb-5 mt-[-12px] text-[13px] text-muted">{{ cl.description }}</p>
        }
      } @else {
        <div class="mb-5 space-y-2.5 rounded-card border border-border bg-raised p-4">
          <div class="grid grid-cols-[1fr_100px_100px] gap-2.5">
            <input
              class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent"
              [(ngModel)]="headerName"
              name="headerName"
              placeholder="Name"
            />
            <input
              type="color"
              class="h-[38px] w-full rounded-lg border border-border bg-surface"
              [(ngModel)]="headerColor"
              name="headerColor"
            />
            <input
              class="rounded-lg border border-border bg-surface px-3 py-2 text-center text-[13px] text-text outline-none focus:border-accent"
              [(ngModel)]="headerIcon"
              name="headerIcon"
              placeholder="icon"
            />
          </div>
          <textarea
            class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            rows="2"
            [(ngModel)]="headerDescription"
            name="headerDescription"
            placeholder="Description (optional)"
          ></textarea>
          <div class="flex justify-end gap-2">
            <button type="button" (click)="editingHeader = false" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">
              Cancel
            </button>
            <button
              type="button"
              (click)="saveHeader(cl)"
              class="rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white"
            >
              Save
            </button>
          </div>
        </div>
      }

      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Items</h3>
          <span class="rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">
            {{ cl.items.length }}
          </span>
          <button
            type="button"
            (click)="startAddItem()"
            class="ml-auto rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]"
          >
            + Add item
          </button>
        </div>

        @if (addingItem) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <app-checklist-item-form
              [checklistId]="cl.id"
              (saved)="onItemSaved($event)"
              (cancelled)="addingItem = false"
            ></app-checklist-item-form>
          </div>
        }

        @if (cl.items.length === 0 && !addingItem) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">No items yet — add your first one above.</div>
        }

        @for (item of cl.items; track item.id) {
          @if (editingItemId === item.id) {
            <div class="border-b border-border bg-raised2/40 p-4 last:border-b-0">
              <app-checklist-item-form
                [checklistId]="cl.id"
                [item]="item"
                (saved)="onItemSaved($event)"
                (cancelled)="editingItemId = null"
              ></app-checklist-item-form>
            </div>
          } @else {
            <div class="flex items-start gap-3 border-b border-border px-4 py-3 last:border-b-0">
              <span class="mt-1 h-3.5 w-[3px] flex-none rounded" [style.background]="priorityColor(item.priority)"></span>
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium" [class.text-muted]="!item.isActive" [class.line-through]="!item.isActive">
                  {{ item.title }}
                </b>
                <div class="mt-1 flex flex-wrap items-center gap-2 text-[10.5px] text-muted">
                  <span>{{ anchorLabel(item) }}</span>
                  <span>·</span>
                  <span class="text-accent">↻ {{ recurrenceLabel(item) }}</span>
                  @if (!item.isActive) {
                    <span class="text-warning">Inactive</span>
                  }
                </div>
              </div>
              <button
                type="button"
                (click)="setActive(item, !item.isActive)"
                class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted transition-colors hover:text-text"
              >
                {{ item.isActive ? 'Deactivate' : 'Activate' }}
              </button>
              <button
                type="button"
                (click)="editingItemId = item.id"
                class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted transition-colors hover:text-text"
              >
                Edit
              </button>
              <button
                type="button"
                (click)="removeItem(item)"
                class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger transition-colors hover:bg-raised2"
              >
                Delete
              </button>
            </div>
          }
        }
      </div>

      @if (error()) {
        <div class="mt-3 text-[12px] font-medium text-danger">{{ error() }}</div>
      }
    } @else if (loading()) {
      <p class="text-[13px] text-muted">Loading checklist…</p>
    } @else {
      <p class="text-[13px] text-danger">Checklist not found.</p>
    }
  `
})
export class ChecklistDetailPageComponent implements OnInit {
  private readonly api = inject(ChecklistsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly checklist = signal<ChecklistWithItems | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected editingHeader = false;
  protected headerName = '';
  protected headerDescription = '';
  protected headerColor = '#6366f1';
  protected headerIcon = '';

  protected addingItem = false;
  protected editingItemId: string | null = null;

  private id = '';

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    if (!this.id) return;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.get(this.id).subscribe({
      next: (cl) => {
        this.checklist.set(cl);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  protected priorityColor(priority: string): string {
    return PRIORITY_COLOR[priority] ?? '#94a3b8';
  }

  protected anchorLabel(item: ChecklistItem): string {
    return anchorSummary(item);
  }

  protected recurrenceLabel(item: ChecklistItem): string {
    return recurrenceSummary(item);
  }

  protected startEditHeader(): void {
    const cl = this.checklist();
    if (!cl) return;
    this.headerName = cl.name;
    this.headerDescription = cl.description ?? '';
    this.headerColor = cl.color ?? '#6366f1';
    this.headerIcon = cl.icon ?? '';
    this.editingHeader = true;
  }

  protected saveHeader(cl: Checklist): void {
    const name = this.headerName.trim();
    if (!name) return;
    this.api
      .update(cl.id, {
        name,
        description: this.headerDescription.trim() || null,
        color: this.headerColor,
        icon: this.headerIcon.trim() || null,
        sortOrder: cl.sortOrder
      })
      .subscribe({
        next: (updated) => {
          this.checklist.update((current) => (current ? { ...current, ...updated } : current));
          this.editingHeader = false;
        },
        error: () => this.error.set('Could not save checklist.')
      });
  }

  protected toggleArchive(cl: Checklist): void {
    this.api.archive(cl.id, !cl.isArchived).subscribe({
      next: (updated) => this.checklist.update((current) => (current ? { ...current, ...updated } : current)),
      error: () => this.error.set('Could not update checklist.')
    });
  }

  protected deleteChecklist(cl: Checklist): void {
    this.api.remove(cl.id).subscribe({
      next: () => this.router.navigateByUrl('/checklists'),
      error: () => this.error.set('Could not delete checklist.')
    });
  }

  protected startAddItem(): void {
    this.editingItemId = null;
    this.addingItem = true;
  }

  protected onItemSaved(item: ChecklistItem): void {
    this.checklist.update((current) => {
      if (!current) return current;
      const exists = current.items.some((i) => i.id === item.id);
      const items = exists ? current.items.map((i) => (i.id === item.id ? item : i)) : [...current.items, item];
      return { ...current, items };
    });
    this.addingItem = false;
    this.editingItemId = null;
  }

  protected setActive(item: ChecklistItem, active: boolean): void {
    this.api.setItemActive(item.id, active).subscribe({
      next: (updated) => this.onItemSaved(updated),
      error: () => this.error.set('Could not update item.')
    });
  }

  protected removeItem(item: ChecklistItem): void {
    this.api.removeItem(item.id).subscribe({
      next: () => this.checklist.update((current) => (current ? { ...current, items: current.items.filter((i) => i.id !== item.id) } : current)),
      error: () => this.error.set('Could not delete item.')
    });
  }
}
