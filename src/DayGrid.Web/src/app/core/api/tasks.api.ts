import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Matches DayGrid.Api's simple_tasks DTO exactly (plan section 4.2b / 5.5b).
// Field names are camelCase because the API serializes with the default
// System.Text.Json camelCase policy.
export type TaskPriority = 'Low' | 'Normal' | 'High' | 'Critical';
export type TaskStatus = 'Open' | 'Done';

export interface SimpleTask {
  id: string;
  title: string;
  notes: string | null;
  priority: TaskPriority;
  status: TaskStatus;
  sortOrder: number;
  completedAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CreateTaskRequest {
  title: string;
  notes?: string | null;
  priority?: TaskPriority;
}

export interface UpdateTaskRequest {
  title: string;
  notes?: string | null;
  priority?: TaskPriority;
}

export interface ReorderTaskItem {
  id: string;
  sortOrder: number;
}

const BASE = '/api/v1/tasks';

// Deliberately isolated per plan section 5.5b — no dependency on DayPlanBuilder,
// RecurrenceEngine, or anything Today-related. Only the Tasks feature talks to this.
@Injectable({ providedIn: 'root' })
export class TasksApi {
  private readonly http = inject(HttpClient);

  list(status?: TaskStatus, q?: string): Observable<SimpleTask[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (q) params = params.set('q', q);
    return this.http.get<SimpleTask[]>(BASE, { params });
  }

  create(req: CreateTaskRequest): Observable<SimpleTask> {
    return this.http.post<SimpleTask>(BASE, req);
  }

  update(id: string, req: UpdateTaskRequest): Observable<SimpleTask> {
    return this.http.put<SimpleTask>(`${BASE}/${id}`, req);
  }

  updateStatus(id: string, status: TaskStatus): Observable<SimpleTask> {
    return this.http.patch<SimpleTask>(`${BASE}/${id}/status`, { status });
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${id}`);
  }

  reorder(items: ReorderTaskItem[]): Observable<void> {
    return this.http.put<void>(`${BASE}/reorder`, items);
  }
}
