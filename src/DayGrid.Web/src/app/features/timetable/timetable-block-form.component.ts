import { Component, EventEmitter, Input, OnChanges, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import { BlockCategoryName, BlockRequest, TimetableApi, TimetableBlock } from '../../core/api/timetable.api';

const CATEGORIES: BlockCategoryName[] = ['Work', 'Personal', 'Health', 'Learning', 'Break', 'Sleep', 'Other'];

function toTimeInput(value: string | null | undefined): string {
  return value ? value.slice(0, 5) : '';
}
function toApiTime(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

// Add/edit form for a single timetable block, matching CreateBlockRequest /
// UpdateBlockRequest field-for-field (title, start/end time, category,
// color, location, checklist link, overlap/notify flags, sort order).
@Component({
  selector: 'app-timetable-block-form',
  imports: [FormsModule],
  template: `
    <form class="space-y-3" (submit)="save($event)">
      <div class="grid grid-cols-[1fr_130px] gap-2.5">
        <input
          class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
          placeholder="Block title"
          [(ngModel)]="title"
          name="title"
          required
        />
        <select
          class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          [(ngModel)]="category"
          name="category"
        >
          @for (c of categories; track c) {
            <option [value]="c">{{ c }}</option>
          }
        </select>
      </div>

      <div class="flex items-center gap-2">
        <input
          type="time"
          class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          [(ngModel)]="startTime"
          name="startTime"
          required
        />
        <span class="text-[12px] text-muted">to</span>
        <input
          type="time"
          class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          [(ngModel)]="endTime"
          name="endTime"
          required
        />
      </div>

      <div class="grid grid-cols-[80px_1fr] gap-2.5">
        <input type="color" class="h-[38px] w-full rounded-lg border border-border bg-surface" [(ngModel)]="color" name="color" />
        <input
          class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          placeholder="Location (optional)"
          [(ngModel)]="location"
          name="location"
        />
      </div>

      <div class="flex flex-wrap gap-4 text-[12.5px] text-text">
        <label class="flex items-center gap-2">
          <input type="checkbox" [(ngModel)]="allowOverlap" name="allowOverlap" />
          Allow overlap
        </label>
        <label class="flex items-center gap-2">
          <input type="checkbox" [(ngModel)]="notifyAtStart" name="notifyAtStart" />
          Notify at start
        </label>
      </div>

      @if (error) {
        <div class="text-[12px] font-medium text-danger">{{ error }}</div>
      }

      <div class="flex justify-end gap-2 pt-1">
        <button
          type="button"
          (click)="cancelled.emit()"
          class="rounded-lg border border-border px-3.5 py-2 text-[12.5px] font-semibold text-muted transition-colors hover:text-text"
        >
          Cancel
        </button>
        <button
          type="submit"
          [disabled]="saving"
          class="rounded-lg bg-accent px-3.5 py-2 text-[12.5px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px disabled:opacity-60"
        >
          {{ saving ? 'Saving…' : isEdit ? 'Save block' : 'Add block' }}
        </button>
      </div>
    </form>
  `
})
export class TimetableBlockFormComponent implements OnChanges {
  private readonly api = inject(TimetableApi);

  @Input({ required: true }) templateId!: string;
  @Input() block: TimetableBlock | null = null;
  @Input() nextSortOrder = 0;
  @Output() saved = new EventEmitter<TimetableBlock>();
  @Output() cancelled = new EventEmitter<void>();

  protected readonly categories = CATEGORIES;

  protected title = '';
  protected category: BlockCategoryName = 'Other';
  protected startTime = '';
  protected endTime = '';
  protected color = '#3b82f6';
  protected location = '';
  protected allowOverlap = false;
  protected notifyAtStart = false;
  protected saving = false;
  protected error: string | null = null;

  protected get isEdit(): boolean {
    return !!this.block;
  }

  ngOnChanges(): void {
    const block = this.block;
    this.title = block?.title ?? '';
    this.category = block?.category ?? 'Other';
    this.startTime = toTimeInput(block?.startTime);
    this.endTime = toTimeInput(block?.endTime);
    this.color = block?.color ?? '#3b82f6';
    this.location = block?.location ?? '';
    this.allowOverlap = block?.allowOverlap ?? false;
    this.notifyAtStart = block?.notifyAtStart ?? false;
    this.error = null;
  }

  protected save(event: Event): void {
    event.preventDefault();
    const title = this.title.trim();
    if (!title || !this.startTime || !this.endTime) return;

    const request: BlockRequest = {
      title,
      startTime: toApiTime(this.startTime),
      endTime: toApiTime(this.endTime),
      category: this.category,
      color: this.color || null,
      location: this.location.trim() || null,
      checklistId: this.block?.checklistId ?? null,
      allowOverlap: this.allowOverlap,
      notifyAtStart: this.notifyAtStart,
      sortOrder: this.block?.sortOrder ?? this.nextSortOrder
    };

    this.saving = true;
    this.error = null;
    const call = this.isEdit ? this.api.updateBlock(this.block!.id, request) : this.api.createBlock(this.templateId, request);
    call.subscribe({
      next: (saved) => {
        this.saving = false;
        this.saved.emit(saved);
      },
      error: (err) => {
        this.saving = false;
        this.error = err?.status === 409 ? 'Overlaps an existing block — enable "Allow overlap" or change the time.' : 'Could not save block.';
      }
    });
  }
}
