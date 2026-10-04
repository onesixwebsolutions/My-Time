import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Full CRUD client for /api/v1/checklists and /api/v1/items — mirrors
// ChecklistsEndpoints.cs exactly. Enum-valued fields (priority, anchorType,
// recurrence.type, recurrence.daysOfWeek) are PascalCase strings on the
// wire — Program.cs registers a global JsonStringEnumConverter — not the
// numbers the DB columns store them as internally.

export type DayOfWeekName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';
export type PriorityName = 'Low' | 'Normal' | 'High' | 'Critical';
export type AnchorTypeName = 'Anytime' | 'FixedTime' | 'TimeWindow' | 'LinkedToBlock';
export type RecurrenceTypeName = 'None' | 'Daily' | 'Weekly' | 'MonthlyByDay' | 'MonthlyByWeekday' | 'EveryNDays' | 'Custom';

export interface NthWeekday {
  nth: number;
  dayOfWeek: DayOfWeekName;
}

export interface RecurrenceRule {
  type: RecurrenceTypeName;
  interval: number;
  daysOfWeek: DayOfWeekName[];
  dayOfMonth: number | null;
  nthWeekday: NthWeekday | null;
  startDate: string | null;
  endDate: string | null;
  exceptionDates: string[];
}

export function defaultRecurrence(): RecurrenceRule {
  return {
    type: 'None',
    interval: 1,
    daysOfWeek: [],
    dayOfMonth: null,
    nthWeekday: null,
    startDate: null,
    endDate: null,
    exceptionDates: []
  };
}

export interface ChecklistItem {
  id: string;
  checklistId: string;
  title: string;
  notes: string | null;
  priority: PriorityName;
  estimatedMinutes: number | null;
  anchorType: AnchorTypeName;
  anchorTime: string | null;
  windowStart: string | null;
  windowEnd: string | null;
  timetableBlockId: string | null;
  recurrence: RecurrenceRule;
  dueDate: string | null;
  reminderOffsetMinutes: number | null;
  isActive: boolean;
  sortOrder: number;
}

export interface Checklist {
  id: string;
  name: string;
  description: string | null;
  color: string | null;
  icon: string | null;
  sortOrder: number;
  isArchived: boolean;
  itemCount?: number;
  completedTodayCount?: number;
}

export interface ChecklistWithItems extends Checklist {
  items: ChecklistItem[];
}

export interface ChecklistRequest {
  name: string;
  description?: string | null;
  color?: string | null;
  icon?: string | null;
  sortOrder?: number;
}

export interface ChecklistItemRequest {
  title: string;
  notes?: string | null;
  priority: PriorityName;
  estimatedMinutes?: number | null;
  anchorType: AnchorTypeName;
  anchorTime?: string | null;
  windowStart?: string | null;
  windowEnd?: string | null;
  timetableBlockId?: string | null;
  recurrence: RecurrenceRule;
  dueDate?: string | null;
  reminderOffsetMinutes?: number | null;
  isActive?: boolean;
}

export interface CompleteItemRequest {
  date: string;
  status?: 'Done' | 'Skipped' | 'Partial';
  note?: string | null;
}

const BASE = '/api/v1/checklists';
const ITEMS_BASE = '/api/v1/items';

@Injectable({ providedIn: 'root' })
export class ChecklistsApi {
  private readonly http = inject(HttpClient);

  list(includeArchived = false): Observable<Checklist[]> {
    const params = new HttpParams().set('includeArchived', includeArchived);
    return this.http.get<Checklist[]>(BASE, { params });
  }

  get(id: string): Observable<ChecklistWithItems> {
    return this.http.get<ChecklistWithItems>(`${BASE}/${id}`);
  }

  create(request: ChecklistRequest): Observable<Checklist> {
    return this.http.post<Checklist>(BASE, request);
  }

  update(id: string, request: ChecklistRequest): Observable<Checklist> {
    return this.http.put<Checklist>(`${BASE}/${id}`, request);
  }

  archive(id: string, archived: boolean): Observable<Checklist> {
    const params = new HttpParams().set('archived', archived);
    return this.http.patch<Checklist>(`${BASE}/${id}/archive`, null, { params });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${id}`);
  }

  createItem(checklistId: string, request: ChecklistItemRequest): Observable<ChecklistItem> {
    return this.http.post<ChecklistItem>(`${BASE}/${checklistId}/items`, request);
  }

  updateItem(itemId: string, request: ChecklistItemRequest): Observable<ChecklistItem> {
    return this.http.put<ChecklistItem>(`${ITEMS_BASE}/${itemId}`, { ...request, isActive: request.isActive ?? true });
  }

  removeItem(itemId: string): Observable<void> {
    return this.http.delete<void>(`${ITEMS_BASE}/${itemId}`);
  }

  setItemActive(itemId: string, active: boolean): Observable<ChecklistItem> {
    const params = new HttpParams().set('active', active);
    return this.http.patch<ChecklistItem>(`${ITEMS_BASE}/${itemId}/active`, null, { params });
  }

  completeItem(itemId: string, req: CompleteItemRequest): Observable<void> {
    return this.http.post<void>(`${ITEMS_BASE}/${itemId}/complete`, req);
  }

  uncompleteItem(itemId: string, date: string): Observable<void> {
    const params = new HttpParams().set('date', date);
    return this.http.delete<void>(`${ITEMS_BASE}/${itemId}/complete`, { params });
  }
}
