import { Component, EventEmitter, Input, Output } from '@angular/core';

// Reusable empty state — "Every list has a designed empty state ... never
// a blank box" (plan section 6.5). Used today by the Tasks page; intended
// to be reused by Checklists/Upcoming/Timetable once those get real lists.
@Component({
  selector: 'app-empty-state',
  standalone: true,
  template: `
    <div class="flex flex-col items-center gap-3 px-6 py-12 text-center">
      <div class="grid h-12 w-12 place-items-center rounded-full bg-raised2 text-2xl">✓</div>
      <p class="max-w-xs text-[13px] text-muted">{{ message }}</p>
      @if (actionLabel) {
        <button
          type="button"
          (click)="action.emit()"
          class="mt-1 inline-flex items-center gap-2 rounded-[9px] bg-accent px-3.5 py-2 text-[12.8px] font-semibold text-white shadow-[0_2px_8px_rgba(99,102,241,.3)] transition-transform hover:-translate-y-px"
        >
          {{ actionLabel }}
        </button>
      }
    </div>
  `
})
export class EmptyStateComponent {
  @Input() message = 'Nothing here yet.';
  @Input() actionLabel?: string;
  @Output() action = new EventEmitter<void>();
}
