import { Component, EventEmitter, Input, OnChanges, Output, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';

import {
  AnchorTypeName,
  ChecklistItem,
  ChecklistItemRequest,
  ChecklistsApi,
  DayOfWeekName,
  PriorityName,
  RecurrenceRule,
  RecurrenceTypeName,
  defaultRecurrence
} from '../../core/api/checklists.api';

const DAYS: DayOfWeekName[] = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
const DAY_SHORT: Record<DayOfWeekName, string> = {
  Sunday: 'Su',
  Monday: 'Mo',
  Tuesday: 'Tu',
  Wednesday: 'We',
  Thursday: 'Th',
  Friday: 'Fr',
  Saturday: 'Sa'
};

function toTimeInput(value: string | null): string {
  return value ? value.slice(0, 5) : '';
}
function toApiTime(value: string): string | null {
  return value ? `${value}:00` : null;
}
function toDateInput(value: string | null): string {
  return value ? value.slice(0, 10) : '';
}

// Add/edit form for a single checklist item — anchor (Anytime / Fixed time /
// Window / Linked to block) and recurrence (None / Daily / Weekly /
// MonthlyByDay / MonthlyByWeekday / EveryNDays / Custom) each drive their own
// conditional fields, matching CreateChecklistItemRequest /
// UpdateChecklistItemRequest field-for-field.
@Component({
  selector: 'app-checklist-item-form',
  standalone: true,
  imports: [FormsModule],
  template: `
    <form class="space-y-3" (submit)="save($event)">
      <div class="grid grid-cols-[1fr_140px] gap-2.5">
        <input
          class="rounded-lg border border-border bg-surface px-3 py-2 text-[13px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
          placeholder="Item title"
          [(ngModel)]="title"
          name="title"
          required
        />
        <select
          class="rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          [(ngModel)]="priority"
          name="priority"
        >
          <option value="Low">Low</option>
          <option value="Normal">Normal</option>
          <option value="High">High</option>
          <option value="Critical">Critical</option>
        </select>
      </div>

      <textarea
        class="w-full resize-none rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent focus:ring-2 focus:ring-accent-soft"
        rows="2"
        placeholder="Notes (optional)"
        [(ngModel)]="notes"
        name="notes"
      ></textarea>

      <div>
        <label class="mb-1.5 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Anchor</label>
        <div class="flex flex-wrap gap-2">
          @for (opt of anchorOptions; track opt) {
            <button
              type="button"
              (click)="anchorType = opt"
              class="rounded-full border px-2.5 py-1 text-[11.5px] font-semibold transition-colors"
              [class.border-accent]="anchorType === opt"
              [class.text-accent]="anchorType === opt"
              [class.border-border]="anchorType !== opt"
              [class.text-muted]="anchorType !== opt"
            >
              {{ opt }}
            </button>
          }
        </div>

        @if (anchorType === 'FixedTime') {
          <input
            type="time"
            class="mt-2 rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="anchorTime"
            name="anchorTime"
          />
        }
        @if (anchorType === 'TimeWindow') {
          <div class="mt-2 flex items-center gap-2">
            <input
              type="time"
              class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
              [(ngModel)]="windowStart"
              name="windowStart"
            />
            <span class="text-[12px] text-muted">to</span>
            <input
              type="time"
              class="rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
              [(ngModel)]="windowEnd"
              name="windowEnd"
            />
          </div>
        }
        @if (anchorType === 'LinkedToBlock') {
          <input
            class="mt-2 w-full rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            placeholder="Timetable block ID (optional — link from the Timetable page instead if unsure)"
            [(ngModel)]="timetableBlockId"
            name="timetableBlockId"
          />
        }
      </div>

      <div>
        <label class="mb-1.5 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Recurrence</label>
        <select
          class="w-full rounded-lg border border-border bg-surface px-2.5 py-2 text-[12.5px] text-text outline-none focus:border-accent"
          [(ngModel)]="recurrenceType"
          name="recurrenceType"
        >
          <option value="None">Does not repeat</option>
          <option value="Daily">Daily</option>
          <option value="Weekly">Weekly</option>
          <option value="MonthlyByDay">Monthly (day of month)</option>
          <option value="MonthlyByWeekday">Monthly (nth weekday)</option>
          <option value="EveryNDays">Every N days</option>
          <option value="Custom">Custom (specific weekdays)</option>
        </select>

        @if (recurrenceType === 'Weekly' || recurrenceType === 'Custom') {
          <div class="mt-2 flex flex-wrap gap-1.5">
            @for (day of days; track day) {
              <button
                type="button"
                (click)="toggleDay(day)"
                class="grid h-7 w-7 place-items-center rounded-full border text-[11px] font-bold transition-colors"
                [class.bg-accent]="daysOfWeek.includes(day)"
                [class.text-white]="daysOfWeek.includes(day)"
                [class.border-accent]="daysOfWeek.includes(day)"
                [class.border-border]="!daysOfWeek.includes(day)"
                [class.text-muted]="!daysOfWeek.includes(day)"
              >
                {{ dayShort[day] }}
              </button>
            }
          </div>
        }

        @if (recurrenceType === 'Daily' || recurrenceType === 'EveryNDays') {
          <div class="mt-2 flex items-center gap-2 text-[12.5px] text-muted">
            Every
            <input
              type="number"
              min="1"
              class="w-16 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
              [(ngModel)]="interval"
              name="interval"
            />
            {{ recurrenceType === 'Daily' ? 'day(s)' : 'day(s)' }}
          </div>
        }

        @if (recurrenceType === 'MonthlyByDay') {
          <div class="mt-2 flex items-center gap-2 text-[12.5px] text-muted">
            On day
            <input
              type="number"
              min="1"
              max="31"
              class="w-16 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
              [(ngModel)]="dayOfMonth"
              name="dayOfMonth"
            />
            of the month
          </div>
        }

        @if (recurrenceType === 'MonthlyByWeekday') {
          <div class="mt-2 flex items-center gap-2 text-[12.5px] text-muted">
            The
            <input
              type="number"
              min="1"
              max="5"
              class="w-14 rounded-lg border border-border bg-surface px-2 py-1.5 text-center text-text outline-none focus:border-accent"
              [(ngModel)]="nth"
              name="nth"
            />
            <select
              class="rounded-lg border border-border bg-surface px-2 py-1.5 text-text outline-none focus:border-accent"
              [(ngModel)]="nthWeekday"
              name="nthWeekday"
            >
              @for (day of days; track day) {
                <option [value]="day">{{ day }}</option>
              }
            </select>
            of the month
          </div>
        }

        @if (recurrenceType !== 'None') {
          <div class="mt-2 flex items-center gap-2 text-[11.5px] text-muted">
            <span>Starts</span>
            <input
              type="date"
              class="rounded-lg border border-border bg-surface px-2 py-1.5 text-text outline-none focus:border-accent"
              [(ngModel)]="startDate"
              name="startDate"
            />
            <span>Ends</span>
            <input
              type="date"
              class="rounded-lg border border-border bg-surface px-2 py-1.5 text-text outline-none focus:border-accent"
              [(ngModel)]="endDate"
              name="endDate"
            />
          </div>
        }
      </div>

      <div class="grid grid-cols-2 gap-2.5">
        <div>
          <label class="mb-1 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Due date</label>
          <input
            type="date"
            class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="dueDate"
            name="dueDate"
          />
        </div>
        <div>
          <label class="mb-1 block text-[10.5px] font-bold uppercase tracking-wider text-muted">Reminder (min before)</label>
          <input
            type="number"
            min="0"
            class="w-full rounded-lg border border-border bg-surface px-3 py-2 text-[12.5px] text-text outline-none focus:border-accent"
            [(ngModel)]="reminderOffsetMinutes"
            name="reminderOffsetMinutes"
          />
        </div>
      </div>

      @if (isEdit) {
        <label class="flex items-center gap-2 text-[12.5px] text-text">
          <input type="checkbox" [(ngModel)]="isActive" name="isActive" />
          Active
        </label>
      }

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
          {{ saving ? 'Saving…' : isEdit ? 'Save item' : 'Add item' }}
        </button>
      </div>
    </form>
  `
})
export class ChecklistItemFormComponent implements OnChanges {
  private readonly api = inject(ChecklistsApi);

  @Input({ required: true }) checklistId!: string;
  @Input() item: ChecklistItem | null = null;
  @Output() saved = new EventEmitter<ChecklistItem>();
  @Output() cancelled = new EventEmitter<void>();

  protected readonly days = DAYS;
  protected readonly dayShort = DAY_SHORT;
  protected readonly anchorOptions: AnchorTypeName[] = ['Anytime', 'FixedTime', 'TimeWindow', 'LinkedToBlock'];

  protected title = '';
  protected notes = '';
  protected priority: PriorityName = 'Normal';
  protected anchorType: AnchorTypeName = 'Anytime';
  protected anchorTime = '';
  protected windowStart = '';
  protected windowEnd = '';
  protected timetableBlockId = '';
  protected recurrenceType: RecurrenceTypeName = 'None';
  protected daysOfWeek: DayOfWeekName[] = [];
  protected interval = 1;
  protected dayOfMonth = 1;
  protected nth = 1;
  protected nthWeekday: DayOfWeekName = 'Monday';
  protected startDate = '';
  protected endDate = '';
  protected dueDate = '';
  protected reminderOffsetMinutes: number | null = null;
  protected isActive = true;
  protected saving = false;
  protected error: string | null = null;

  protected get isEdit(): boolean {
    return !!this.item;
  }

  ngOnChanges(): void {
    const item = this.item;
    this.title = item?.title ?? '';
    this.notes = item?.notes ?? '';
    this.priority = item?.priority ?? 'Normal';
    this.anchorType = item?.anchorType ?? 'Anytime';
    this.anchorTime = toTimeInput(item?.anchorTime ?? null);
    this.windowStart = toTimeInput(item?.windowStart ?? null);
    this.windowEnd = toTimeInput(item?.windowEnd ?? null);
    this.timetableBlockId = item?.timetableBlockId ?? '';

    const recurrence: RecurrenceRule = item?.recurrence ?? defaultRecurrence();
    this.recurrenceType = recurrence.type;
    this.daysOfWeek = [...recurrence.daysOfWeek];
    this.interval = recurrence.interval || 1;
    this.dayOfMonth = recurrence.dayOfMonth ?? 1;
    this.nth = recurrence.nthWeekday?.nth ?? 1;
    this.nthWeekday = recurrence.nthWeekday?.dayOfWeek ?? 'Monday';
    this.startDate = toDateInput(recurrence.startDate);
    this.endDate = toDateInput(recurrence.endDate);

    this.dueDate = toDateInput(item?.dueDate ?? null);
    this.reminderOffsetMinutes = item?.reminderOffsetMinutes ?? null;
    this.isActive = item?.isActive ?? true;
    this.error = null;
  }

  protected toggleDay(day: DayOfWeekName): void {
    this.daysOfWeek = this.daysOfWeek.includes(day)
      ? this.daysOfWeek.filter((d) => d !== day)
      : [...this.daysOfWeek, day];
  }

  protected save(event: Event): void {
    event.preventDefault();
    const title = this.title.trim();
    if (!title) return;

    const recurrence: RecurrenceRule = {
      type: this.recurrenceType,
      interval: this.recurrenceType === 'Daily' || this.recurrenceType === 'EveryNDays' ? Number(this.interval) || 1 : 1,
      daysOfWeek: this.recurrenceType === 'Weekly' || this.recurrenceType === 'Custom' ? this.daysOfWeek : [],
      dayOfMonth: this.recurrenceType === 'MonthlyByDay' ? Number(this.dayOfMonth) || 1 : null,
      nthWeekday:
        this.recurrenceType === 'MonthlyByWeekday' ? { nth: Number(this.nth) || 1, dayOfWeek: this.nthWeekday } : null,
      startDate: this.recurrenceType !== 'None' && this.startDate ? this.startDate : null,
      endDate: this.recurrenceType !== 'None' && this.endDate ? this.endDate : null,
      exceptionDates: this.item?.recurrence.exceptionDates ?? []
    };

    const request: ChecklistItemRequest = {
      title,
      notes: this.notes.trim() || null,
      priority: this.priority,
      estimatedMinutes: this.item?.estimatedMinutes ?? null,
      anchorType: this.anchorType,
      anchorTime: this.anchorType === 'FixedTime' ? toApiTime(this.anchorTime) : null,
      windowStart: this.anchorType === 'TimeWindow' ? toApiTime(this.windowStart) : null,
      windowEnd: this.anchorType === 'TimeWindow' ? toApiTime(this.windowEnd) : null,
      timetableBlockId: this.anchorType === 'LinkedToBlock' ? this.timetableBlockId.trim() || null : null,
      recurrence,
      dueDate: this.dueDate || null,
      reminderOffsetMinutes: this.reminderOffsetMinutes === null || (this.reminderOffsetMinutes as unknown) === '' ? null : Number(this.reminderOffsetMinutes),
      isActive: this.isActive
    };

    this.saving = true;
    this.error = null;
    const call = this.isEdit ? this.api.updateItem(this.item!.id, request) : this.api.createItem(this.checklistId, request);
    call.subscribe({
      next: (saved) => {
        this.saving = false;
        this.saved.emit(saved);
      },
      error: () => {
        this.saving = false;
        this.error = 'Could not save item.';
      }
    });
  }
}
