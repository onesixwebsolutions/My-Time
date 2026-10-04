import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Shape matches plan section 5.1's GET /api/v1/today JSON example field-for-field.

export interface TodayOverride {
  mode: 'RestDay' | 'UseTemplate' | 'CustomOnly';
  note: string | null;
}

export interface TodayTemplate {
  id: string;
  name: string;
  dayStart: string;
  dayEnd: string;
  slotMinutes: number;
}

export interface NowBlock {
  blockId: string;
  title: string;
  startTime: string;
  endTime: string;
  category: string;
  color: string;
  elapsedMinutes: number;
  remainingMinutes: number;
  progressPercent: number;
}

export interface NextBlock {
  title: string;
  startTime: string;
  startsInMinutes: number;
}

export interface LinkedItem {
  itemId: string;
  title: string;
  isCompleted: boolean;
}

export type BlockState = 'past' | 'current' | 'upcoming';

export interface TimelineBlock {
  id: string;
  title: string;
  startTime: string;
  endTime: string;
  category: string;
  color: string;
  location: string | null;
  state: BlockState;
  linkedItems: LinkedItem[];
}

export interface ChecklistItemDto {
  itemId: string;
  title: string;
  anchorType: 'Anytime' | 'FixedTime' | 'TimeWindow' | 'LinkedToBlock';
  anchorTime: string | null;
  priority: 'Low' | 'Normal' | 'High' | 'Critical';
  estimatedMinutes: number | null;
  isCompleted: boolean;
  completedAt: string | null;
  isOverdue: boolean;
  recurrenceLabel: string | null;
}

export interface TodayChecklist {
  checklistId: string;
  name: string;
  color: string;
  icon: string;
  completedCount: number;
  totalCount: number;
  items: ChecklistItemDto[];
}

export interface DueTodayItem {
  id: string;
  title: string;
  dueTime: string | null;
  priority: 'Low' | 'Normal' | 'High' | 'Critical';
}

export interface TodaySummary {
  totalItems: number;
  completedItems: number;
  completionPercent: number;
  blocksTotal: number;
  blocksDone: number;
  minutesScheduled: number;
}

export interface TodayDto {
  date: string;
  dayOfWeek: string;
  displayDate: string;
  override: TodayOverride | null;
  template: TodayTemplate | null;
  nowBlock: NowBlock | null;
  nextBlock: NextBlock | null;
  blocks: TimelineBlock[];
  checklists: TodayChecklist[];
  dueToday: DueTodayItem[];
  overdue: DueTodayItem[];
  summary: TodaySummary;
}

@Injectable({ providedIn: 'root' })
export class TodayApi {
  private readonly http = inject(HttpClient);

  get(date?: string): Observable<TodayDto> {
    let params = new HttpParams();
    if (date) params = params.set('date', date);
    return this.http.get<TodayDto>('/api/v1/today', { params });
  }
}
