import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { TemplateRequest, TimetableApi, TimetableBlock, TimetableTemplate, TimetableTemplateAndBlocks } from '../../core/api/timetable.api';
import { TimetableBlockFormComponent } from './timetable-block-form.component';

const CATEGORY_COLOR: Record<string, string> = {
  Work: '#3b82f6',
  Personal: '#a855f7',
  Health: '#22c55e',
  Learning: '#f59e0b',
  Break: '#94a3b8',
  Sleep: '#6366f1',
  Other: '#64748b'
};

function toTimeInput(value: string): string {
  return value.slice(0, 5);
}
function toApiTime(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

// Full CRUD for one timetable template's header + its blocks.
@Component({
  selector: 'app-timetable-detail-page',
  standalone: true,
  imports: [FormsModule, RouterLink, TimetableBlockFormComponent],
  template: `
    @if (data(); as d) {
      <a routerLink="/timetable" class="mb-4 inline-block text-[12.5px] font-semibold text-muted hover:text-text">← Timetable</a>

      @if (!editingHeader) {
        <div class="mb-5 flex flex-wrap items-center gap-3">
          <h1 class="text-[22px] font-bold tracking-tight text-text">{{ d.template.name }}</h1>
          @if (d.template.isDefault) {
            <span class="rounded-full px-2 py-0.5 text-[10px] font-bold text-success" style="background:rgba(34,197,94,.15)">Default</span>
          } @else {
            <button type="button" (click)="setDefault(d.template)" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted hover:text-text">
              Set as default
            </button>
          }
          <span class="text-[12px] text-muted">{{ d.template.dayStart.slice(0, 5) }} – {{ d.template.dayEnd.slice(0, 5) }} · {{ d.template.slotMinutes }}min slots</span>
          <button type="button" (click)="startEditHeader(d.template)" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-muted hover:text-text">
            Edit
          </button>
          <button type="button" (click)="deleteTemplate(d.template)" class="rounded-lg border border-border px-2.5 py-1 text-[11.5px] font-semibold text-danger hover:bg-raised2">
            Delete
          </button>
        </div>
        @if (d.template.description) {
          <p class="mb-5 mt-[-12px] text-[13px] text-muted">{{ d.template.description }}</p>
        }
      } @else {
        <div class="mb-5 space-y-2.5 rounded-card border border-border bg-raised p-4">
          <input
            class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent"
            [(ngModel)]="headerName"
            name="headerName"
            placeholder="Template name"
          />
          <textarea
            class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            rows="2"
            [(ngModel)]="headerDescription"
            name="headerDescription"
            placeholder="Description (optional)"
          ></textarea>
          <div class="flex items-center gap-2 text-[12.5px] text-muted">
            Day runs
            <input type="time" class="rounded-lg border border-border bg-surface px-2.5 py-1.5 text-text outline-none focus:border-accent" [(ngModel)]="headerDayStart" name="headerDayStart" />
            to
            <input type="time" class="rounded-lg border border-border bg-surface px-2.5 py-1.5 text-text outline-none focus:border-accent" [(ngModel)]="headerDayEnd" name="headerDayEnd" />
            slot
            <input
              type="number"
              min="5"
              class="w-16 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
              [(ngModel)]="headerSlotMinutes"
              name="headerSlotMinutes"
            />
            min
          </div>
          <div class="flex justify-end gap-2">
            <button type="button" (click)="editingHeader = false" class="rounded-lg border border-border px-3 py-1.5 text-[12px] font-semibold text-muted">Cancel</button>
            <button type="button" (click)="saveHeader(d.template)" class="rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white">Save</button>
          </div>
        </div>
      }

      <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
        <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
          <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Blocks</h3>
          <span class="rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">{{ d.blocks.length }}</span>
          <button
            type="button"
            (click)="addingBlock = true; editingBlockId = null"
            class="ml-auto rounded-lg bg-accent px-3 py-1.5 text-[12px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)]"
          >
            + Add block
          </button>
        </div>

        @if (addingBlock) {
          <div class="border-b border-border bg-raised2/40 p-4">
            <app-timetable-block-form
              [templateId]="d.template.id"
              [nextSortOrder]="d.blocks.length"
              (saved)="onBlockSaved($event)"
              (cancelled)="addingBlock = false"
            ></app-timetable-block-form>
          </div>
        }

        @if (d.blocks.length === 0 && !addingBlock) {
          <div class="px-4 py-10 text-center text-[13px] text-muted">No blocks yet — add your first one above.</div>
        }

        @for (block of d.blocks; track block.id) {
          @if (editingBlockId === block.id) {
            <div class="border-b border-border bg-raised2/40 p-4 last:border-b-0">
              <app-timetable-block-form
                [templateId]="d.template.id"
                [block]="block"
                (saved)="onBlockSaved($event)"
                (cancelled)="editingBlockId = null"
              ></app-timetable-block-form>
            </div>
          } @else {
            <div
              class="flex items-center gap-3 border-b border-l-[3px] border-border px-4 py-3 last:border-b-0"
              [style.border-left-color]="block.color || categoryColor(block.category)"
            >
              <div class="min-w-0 flex-1">
                <b class="block text-[13.2px] font-medium">{{ block.title }}</b>
                <span class="mt-0.5 block text-[11px] text-muted">
                  {{ block.startTime.slice(0, 5) }} – {{ block.endTime.slice(0, 5) }} · {{ block.category }}{{ block.location ? ' · ' + block.location : '' }}
                </span>
              </div>
              <button type="button" (click)="editingBlockId = block.id; addingBlock = false" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-muted hover:text-text">
                Edit
              </button>
              <button type="button" (click)="removeBlock(block)" class="rounded-lg border border-border px-2.5 py-1 text-[11px] font-semibold text-danger hover:bg-raised2">
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
      <p class="text-[13px] text-muted">Loading template…</p>
    } @else {
      <p class="text-[13px] text-danger">Template not found.</p>
    }
  `
})
export class TimetableDetailPageComponent implements OnInit {
  private readonly api = inject(TimetableApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly data = signal<TimetableTemplateAndBlocks | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected editingHeader = false;
  protected headerName = '';
  protected headerDescription = '';
  protected headerDayStart = '06:00';
  protected headerDayEnd = '23:00';
  protected headerSlotMinutes = 30;

  protected addingBlock = false;
  protected editingBlockId: string | null = null;

  private id = '';

  ngOnInit(): void {
    this.id = this.route.snapshot.paramMap.get('id') ?? '';
    if (!this.id) return;
    this.load();
  }

  private load(): void {
    this.loading.set(true);
    this.api.getTemplate(this.id).subscribe({
      next: (d) => {
        this.data.set(d);
        this.loading.set(false);
      },
      error: () => this.loading.set(false)
    });
  }

  protected categoryColor(category: string): string {
    return CATEGORY_COLOR[category] ?? '#64748b';
  }

  protected startEditHeader(template: TimetableTemplate): void {
    this.headerName = template.name;
    this.headerDescription = template.description ?? '';
    this.headerDayStart = toTimeInput(template.dayStart);
    this.headerDayEnd = toTimeInput(template.dayEnd);
    this.headerSlotMinutes = template.slotMinutes;
    this.editingHeader = true;
  }

  protected saveHeader(template: TimetableTemplate): void {
    const name = this.headerName.trim();
    if (!name) return;
    const request: TemplateRequest = {
      name,
      description: this.headerDescription.trim() || null,
      dayStart: toApiTime(this.headerDayStart),
      dayEnd: toApiTime(this.headerDayEnd),
      slotMinutes: Number(this.headerSlotMinutes) || 30
    };
    this.api.updateTemplate(template.id, request).subscribe({
      next: (updated) => {
        this.data.update((current) => (current ? { ...current, template: updated } : current));
        this.editingHeader = false;
      },
      error: () => this.error.set('Could not save template.')
    });
  }

  protected setDefault(template: TimetableTemplate): void {
    this.api.setDefaultTemplate(template.id).subscribe({
      next: (updated) => this.data.update((current) => (current ? { ...current, template: updated } : current)),
      error: () => this.error.set('Could not set default template.')
    });
  }

  protected deleteTemplate(template: TimetableTemplate): void {
    this.api.deleteTemplate(template.id).subscribe({
      next: () => this.router.navigateByUrl('/timetable'),
      error: () => this.error.set('Could not delete template.')
    });
  }

  protected onBlockSaved(block: TimetableBlock): void {
    this.data.update((current) => {
      if (!current) return current;
      const exists = current.blocks.some((b) => b.id === block.id);
      const blocks = exists ? current.blocks.map((b) => (b.id === block.id ? block : b)) : [...current.blocks, block];
      blocks.sort((a, b) => a.startTime.localeCompare(b.startTime));
      return { ...current, blocks };
    });
    this.addingBlock = false;
    this.editingBlockId = null;
  }

  protected removeBlock(block: TimetableBlock): void {
    this.api.deleteBlock(block.id).subscribe({
      next: () => this.data.update((current) => (current ? { ...current, blocks: current.blocks.filter((b) => b.id !== block.id) } : current)),
      error: () => this.error.set('Could not delete block.')
    });
  }
}
