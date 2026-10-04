import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Full CRUD client for /api/v1/timetable/* — mirrors TimetableEndpoints.cs
// exactly. Enum-valued fields (category, scope, dayOfWeek) are PascalCase
// strings on the wire, same convention as checklists.api.ts.

export type BlockCategoryName = 'Work' | 'Personal' | 'Health' | 'Learning' | 'Break' | 'Sleep' | 'Other';
export type AssignmentScopeName = 'Weekday' | 'SpecificDate' | 'DateRange';
export type DayOfWeekName = 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday';

export interface TimetableBlock {
  id: string;
  templateId: string;
  title: string;
  startTime: string;
  endTime: string;
  category: BlockCategoryName;
  color: string | null;
  location: string | null;
  checklistId: string | null;
  allowOverlap: boolean;
  notifyAtStart: boolean;
  sortOrder: number;
}

export interface TimetableTemplate {
  id: string;
  name: string;
  description: string | null;
  isDefault: boolean;
  dayStart: string;
  dayEnd: string;
  slotMinutes: number;
}

export interface TimetableTemplateAndBlocks {
  template: TimetableTemplate;
  blocks: TimetableBlock[];
}

export interface TimetableAssignment {
  id: string;
  templateId: string;
  scope: AssignmentScopeName;
  dayOfWeek: DayOfWeekName | null;
  dateFrom: string | null;
  dateTo: string | null;
  priority: number;
}

export interface TemplateRequest {
  name: string;
  description?: string | null;
  dayStart: string;
  dayEnd: string;
  slotMinutes: number;
}

export interface BlockRequest {
  title: string;
  startTime: string;
  endTime: string;
  category: BlockCategoryName;
  color?: string | null;
  location?: string | null;
  checklistId?: string | null;
  allowOverlap: boolean;
  notifyAtStart: boolean;
  sortOrder: number;
}

export interface AssignmentRequest {
  templateId: string;
  scope: AssignmentScopeName;
  dayOfWeek?: DayOfWeekName | null;
  dateFrom?: string | null;
  dateTo?: string | null;
  priority: number;
}

const BASE = '/api/v1/timetable';

@Injectable({ providedIn: 'root' })
export class TimetableApi {
  private readonly http = inject(HttpClient);

  listTemplates(): Observable<TimetableTemplate[]> {
    return this.http.get<TimetableTemplate[]>(`${BASE}/templates`);
  }

  getTemplate(id: string): Observable<TimetableTemplateAndBlocks> {
    return this.http.get<TimetableTemplateAndBlocks>(`${BASE}/templates/${id}`);
  }

  createTemplate(request: TemplateRequest): Observable<TimetableTemplate> {
    return this.http.post<TimetableTemplate>(`${BASE}/templates`, request);
  }

  updateTemplate(id: string, request: TemplateRequest): Observable<TimetableTemplate> {
    return this.http.put<TimetableTemplate>(`${BASE}/templates/${id}`, request);
  }

  deleteTemplate(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/templates/${id}`);
  }

  setDefaultTemplate(id: string): Observable<TimetableTemplate> {
    return this.http.patch<TimetableTemplate>(`${BASE}/templates/${id}/default`, null);
  }

  listBlocks(templateId: string): Observable<TimetableBlock[]> {
    return this.http.get<TimetableBlock[]>(`${BASE}/templates/${templateId}/blocks`);
  }

  createBlock(templateId: string, request: BlockRequest): Observable<TimetableBlock> {
    return this.http.post<TimetableBlock>(`${BASE}/templates/${templateId}/blocks`, request);
  }

  updateBlock(blockId: string, request: BlockRequest): Observable<TimetableBlock> {
    return this.http.put<TimetableBlock>(`${BASE}/blocks/${blockId}`, request);
  }

  deleteBlock(blockId: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/blocks/${blockId}`);
  }

  listAssignments(): Observable<TimetableAssignment[]> {
    return this.http.get<TimetableAssignment[]>(`${BASE}/assignments`);
  }

  createAssignment(request: AssignmentRequest): Observable<TimetableAssignment> {
    return this.http.post<TimetableAssignment>(`${BASE}/assignments`, request);
  }

  deleteAssignment(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/assignments/${id}`);
  }
}
