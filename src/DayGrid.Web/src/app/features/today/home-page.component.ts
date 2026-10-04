import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

import { ChecklistsApi } from '../../core/api/checklists.api';
import { TodayApi, TodayDto } from '../../core/api/today.api';
import { ClockService } from '../../core/time/clock.service';

// "What should I be doing right now?" — plan section 6.3. One GET /api/v1/today
// call renders the whole page: now-card, timeline rail, checklist column, due
// today panel. The now-card's elapsed/remaining figures are recomputed locally
// against ClockService's 1-second tick so they move without polling; the block
// boundary itself only changes on refetch/SignalR (Phase 5 wires the latter).
@Component({
  selector: 'app-home-page',
  standalone: true,
  template: `
    @if (loading()) {
      <div class="py-16 text-center text-[13px] text-muted">Loading today…</div>
    } @else if (error()) {
      <div class="rounded-card border border-border bg-raised p-6 text-[13px] text-danger">
        {{ error() }}
      </div>
    } @else {
      @if (today(); as day) {
      <div class="mb-5 flex flex-wrap items-center gap-3.5">
        <h1 class="text-[23px] font-bold tracking-tight text-text">{{ day.displayDate }}</h1>
        @if (day.template) {
          <span class="rounded-full border border-border bg-raised px-2.5 py-1 text-[11.5px] font-semibold text-muted">
            {{ day.template.name }} template
          </span>
        }
      </div>

      @if (day.override; as ov) {
        <div
          class="mb-5 rounded-card px-4 py-3 text-[13px] text-warning"
          style="background:rgba(245,158,11,.12);border:1px solid rgba(245,158,11,.35)"
        >
          {{ ov.mode }}{{ ov.note ? ' — ' + ov.note : '' }}
        </div>
      }

      <!-- Now card -->
      @if (day.nowBlock; as now) {
        <div
          class="relative mb-5 overflow-hidden rounded-2xl p-6 text-white shadow-[0_10px_34px_rgba(99,102,241,.30)]"
          style="background:linear-gradient(120deg,#4f46e5 0%,#7c3aed 55%,#a855f7 100%)"
        >
          <div class="mb-2 flex items-center gap-2 text-[11.5px] font-bold uppercase tracking-wider opacity-90">
            <span class="h-2 w-2 rounded-full bg-[#4ade80]"></span>
            Now · <span class="tnum">{{ clockLabel() }}</span>
          </div>
          <div class="flex flex-wrap items-end justify-between gap-5">
            <div>
              <h2 class="text-[27px] font-bold leading-tight tracking-tight">{{ now.title }}</h2>
              <div class="mt-1 text-[12.5px] opacity-80">{{ now.category }} · {{ now.startTime }}–{{ now.endTime }}</div>
            </div>
            <div class="text-right">
              <b class="tnum block text-[23px] font-bold tracking-tight">{{ remainingLabel() }}</b>
              <span class="text-[11px] uppercase tracking-wider opacity-80">Remaining</span>
            </div>
          </div>
          <div class="relative mt-4 h-[5px] rounded-full bg-white/25">
            <div class="h-full rounded-full bg-white" [style.width.%]="liveProgressPercent()"></div>
          </div>
          <div class="mt-2 flex justify-between text-[11.5px] font-semibold opacity-85">
            <span class="tnum">{{ now.startTime }}</span>
            <span>{{ liveProgressPercent() }}% elapsed</span>
            <span class="tnum">{{ now.endTime }}</span>
          </div>
          @if (day.nextBlock; as next) {
            <div class="mt-3.5 flex items-center gap-2 border-t border-white/20 pt-3 text-[12.5px] opacity-90">
              Next at <b class="tnum">{{ next.startTime }}</b> · {{ next.title }}
            </div>
          }
        </div>
      } @else {
        <div class="mb-5 rounded-card border border-border bg-raised p-5 text-[13px] text-muted">
          Nothing scheduled right now.
        </div>
      }

      <!-- Stats -->
      <div class="mb-5 grid grid-cols-2 gap-3 sm:grid-cols-4">
        <div class="rounded-card border border-border bg-raised p-3.5">
          <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Day progress</span>
          <b class="tnum mt-1 block text-[24px] font-bold tracking-tight">{{ day.summary.completionPercent }}%</b>
        </div>
        <div class="rounded-card border border-border bg-raised p-3.5">
          <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Checklist</span>
          <b class="tnum mt-1 block text-[24px] font-bold tracking-tight"
            >{{ day.summary.completedItems }}<small class="text-[13px] text-muted"> / {{ day.summary.totalItems }}</small></b
          >
        </div>
        <div class="rounded-card border border-border bg-raised p-3.5">
          <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Blocks done</span>
          <b class="tnum mt-1 block text-[24px] font-bold tracking-tight"
            >{{ day.summary.blocksDone }}<small class="text-[13px] text-muted"> / {{ day.summary.blocksTotal }}</small></b
          >
        </div>
        <div class="rounded-card border border-border bg-raised p-3.5">
          <span class="text-[10.5px] font-bold uppercase tracking-wider text-muted">Scheduled</span>
          <b class="tnum mt-1 block text-[24px] font-bold tracking-tight">{{ scheduledLabel(day.summary.minutesScheduled) }}</b>
        </div>
      </div>

      <div class="grid items-start gap-5 lg:grid-cols-[1.32fr_1fr]">
        <!-- Timeline -->
        <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
          <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
            <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Timetable</h3>
          </div>
          <ol role="list" class="max-h-[820px] overflow-y-auto p-3">
            @for (block of day.blocks; track block.id) {
              <li
                role="listitem"
                class="mb-2 rounded-lg border-l-[3px] p-3 last:mb-0"
                [style.border-left-color]="block.color"
                [style.background]="block.color + '1a'"
                [class.opacity-50]="block.state === 'past'"
                [class.ring-2]="block.state === 'current'"
                [class.ring-accent]="block.state === 'current'"
              >
                <b class="block text-[13px] font-semibold">{{ block.title }}</b>
                <span class="mt-0.5 block text-[11px] text-muted">
                  {{ block.startTime }} – {{ block.endTime }}{{ block.location ? ' · ' + block.location : '' }}
                </span>
                @if (block.linkedItems.length) {
                  <div class="mt-1.5 flex flex-wrap gap-1.5">
                    @for (li of block.linkedItems; track li.itemId) {
                      <span
                        class="rounded-full px-2 py-0.5 text-[10.5px]"
                        [class.line-through]="li.isCompleted"
                        [class.text-muted]="li.isCompleted"
                        [class.bg-raised2]="!li.isCompleted"
                      >
                        {{ li.title }}
                      </span>
                    }
                  </div>
                }
              </li>
            } @empty {
              <li class="px-3 py-8 text-center text-[13px] text-muted">No blocks scheduled today.</li>
            }
          </ol>
        </div>

        <!-- Checklist + due today -->
        <div class="flex flex-col gap-4">
          <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
            <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
              <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Checklist</h3>
              <span class="ml-auto rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">
                {{ day.summary.completedItems }} / {{ day.summary.totalItems }}
              </span>
            </div>

            @for (group of day.checklists; track group.checklistId) {
              <div class="border-b border-border last:border-b-0">
                <div class="flex items-center gap-2.5 bg-raised2 px-4 py-2.5">
                  <span class="h-2 w-2 flex-none rounded-[3px]" [style.background]="group.color"></span>
                  <b class="text-[13px] font-semibold">{{ group.name }}</b>
                  <span class="ml-auto text-[11px] font-semibold text-muted">{{ group.completedCount }}/{{ group.totalCount }}</span>
                </div>
                @for (item of group.items; track item.itemId) {
                  <div
                    class="flex cursor-pointer items-start gap-2.5 px-4 py-2.5 transition-colors hover:bg-raised2"
                    (click)="toggleItem(item)"
                  >
                    <span
                      class="mt-0.5 grid h-[17px] w-[17px] flex-none place-items-center rounded-[5px] border-[1.5px] border-border transition-colors"
                      [class.bg-success]="item.isCompleted"
                      [class.border-success]="item.isCompleted"
                    >
                      @if (item.isCompleted) {
                        <svg viewBox="0 0 24 24" fill="none" class="h-[11px] w-[11px] stroke-white" stroke-width="3.2">
                          <path d="m5 13 4 4L19 7" />
                        </svg>
                      }
                    </span>
                    <div class="min-w-0 flex-1">
                      <b
                        class="block text-[13.2px] font-medium"
                        [class.line-through]="item.isCompleted"
                        [class.text-muted]="item.isCompleted"
                        >{{ item.title }}</b
                      >
                      <div class="mt-1 flex flex-wrap items-center gap-2">
                        @if (item.anchorTime) {
                          <span class="text-[10.5px] font-medium text-muted">{{ item.anchorTime }}</span>
                        }
                        @if (item.isOverdue) {
                          <span class="text-[10.5px] font-semibold text-danger">Overdue</span>
                        }
                        @if (item.recurrenceLabel) {
                          <span class="text-[10.5px] font-medium text-accent">↻ {{ item.recurrenceLabel }}</span>
                        }
                      </div>
                    </div>
                  </div>
                }
              </div>
            } @empty {
              <div class="px-4 py-8 text-center text-[13px] text-muted">No checklists for today.</div>
            }
          </div>

          @if (day.dueToday.length) {
            <div class="overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
              <div class="flex items-center gap-2.5 border-b border-border px-4 py-3.5">
                <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Due today</h3>
                <span class="ml-auto rounded-full border border-border bg-raised2 px-2.5 py-1 text-[11px] font-semibold text-muted">
                  {{ day.dueToday.length }}
                </span>
              </div>
              @for (due of day.dueToday; track due.id) {
                <div class="flex items-center gap-2.5 border-b border-border px-4 py-2.5 last:border-b-0">
                  <div class="min-w-0 flex-1">
                    <b class="block text-[13.2px] font-medium">{{ due.title }}</b>
                    <div class="mt-0.5 flex items-center gap-2 text-[10.5px] text-muted">
                      <span>{{ due.dueTime ? due.dueTime : 'All day' }}</span>
                      <span>{{ due.priority }}</span>
                    </div>
                  </div>
                </div>
              }
            </div>
          }
        </div>
      </div>
      }
    }
  `
})
export class HomePageComponent implements OnInit {
  private readonly todayApi = inject(TodayApi);
  private readonly checklistsApi = inject(ChecklistsApi);
  private readonly route = inject(ActivatedRoute);
  protected readonly clock = inject(ClockService);

  protected readonly today = signal<TodayDto | null>(null);
  protected readonly loading = signal(true);
  protected readonly error = signal<string | null>(null);

  protected readonly clockLabel = computed(() => this.clock.now().toTimeString().slice(0, 8));

  ngOnInit(): void {
    const date = this.route.snapshot.paramMap.get('date') ?? undefined;
    this.fetch(date);
  }

  private fetch(date?: string): void {
    this.loading.set(true);
    this.error.set(null);
    this.todayApi.get(date).subscribe({
      next: (dto) => {
        this.today.set(dto);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Could not load today.');
        this.loading.set(false);
      }
    });
  }

  /** Recomputed every clock tick against nowBlock's start/end, so the bar moves without polling. */
  protected liveProgressPercent(): number {
    const now = this.today()?.nowBlock;
    if (!now) return 0;
    const start = this.toMinutesSinceMidnight(now.startTime);
    const end = this.toMinutesSinceMidnight(now.endTime);
    const total = end - start;
    if (total <= 0) return 0;
    const nowMinutes = this.clock.now().getHours() * 60 + this.clock.now().getMinutes() + this.clock.now().getSeconds() / 60;
    const elapsed = Math.min(Math.max(nowMinutes - start, 0), total);
    return Math.round((elapsed / total) * 100);
  }

  protected remainingLabel(): string {
    const now = this.today()?.nowBlock;
    if (!now) return '';
    const end = this.toMinutesSinceMidnight(now.endTime);
    const nowMinutes = this.clock.now().getHours() * 60 + this.clock.now().getMinutes() + this.clock.now().getSeconds() / 60;
    const remaining = Math.max(Math.round(end - nowMinutes), 0);
    const hours = Math.floor(remaining / 60);
    const mins = remaining % 60;
    return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`;
  }

  protected scheduledLabel(minutes: number): string {
    const hours = Math.floor(minutes / 60);
    const mins = minutes % 60;
    return `${hours}h ${mins}m`;
  }

  protected toggleItem(item: { itemId: string; isCompleted: boolean }): void {
    const day = this.today();
    if (!day) return;

    // Optimistic visual toggle (plan section 6.4). Wired to the real
    // POST /items/{id}/complete / DELETE endpoints via ChecklistsApi;
    // on failure the local state reverts.
    const dateStr = day.date;
    const wasCompleted = item.isCompleted;

    this.today.update((current) => {
      if (!current) return current;
      return {
        ...current,
        checklists: current.checklists.map((group) => ({
          ...group,
          items: group.items.map((i) => (i.itemId === item.itemId ? { ...i, isCompleted: !i.isCompleted } : i))
        }))
      };
    });

    const revert = () => {
      this.today.update((current) => {
        if (!current) return current;
        return {
          ...current,
          checklists: current.checklists.map((group) => ({
            ...group,
            items: group.items.map((i) => (i.itemId === item.itemId ? { ...i, isCompleted: wasCompleted } : i))
          }))
        };
      });
    };

    const request = wasCompleted
      ? this.checklistsApi.uncompleteItem(item.itemId, dateStr)
      : this.checklistsApi.completeItem(item.itemId, { date: dateStr });

    request.subscribe({ error: revert });
  }

  private toMinutesSinceMidnight(time: string): number {
    const [h, m] = time.split(':').map(Number);
    return h * 60 + m;
  }
}
