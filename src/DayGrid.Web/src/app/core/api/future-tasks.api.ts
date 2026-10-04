import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Full CRUD client for /api/v1/future-tasks and its /reminders sub-resource —
// mirrors FutureTasksEndpoints.cs exactly. Enum-valued fields are PascalCase
// strings on the wire (same convention as checklists.api.ts /
// timetable.api.ts); `channels` is a [Flags] enum, so System.Text.Json's
// JsonStringEnumConverter serializes it as a comma-joined string, e.g.
// "InApp, Email" or just "InApp" — never a number.

export type TaskPriorityName = 'Low' | 'Normal' | 'High' | 'Critical';
export type FutureTaskStatusName = 'Pending' | 'Done' | 'Cancelled' | 'Deferred';
export type ReminderStatusName = 'Scheduled' | 'Sent' | 'Failed' | 'Cancelled';
export type NotificationChannelName = 'InApp' | 'Email';

export interface FutureTaskReminder {
  id: string;
  futureTaskId: string;
  offsetMinutes: number;
  fireAtUtc: string;
  channels: string;
  status: ReminderStatusName;
  sentAtUtc: string | null;
  attemptCount: number;
}

export interface FutureTask {
  id: string;
  title: string;
  notes: string | null;
  dueDate: string;
  dueTime: string | null;
  category: string | null;
  priority: TaskPriorityName;
  status: FutureTaskStatusName;
  completedAt: string | null;
  promoteToChecklistId: string | null;
  reminders: FutureTaskReminder[];
}

export interface ReminderRequest {
  offsetMinutes: number;
  channels: NotificationChannelName[];
}

export interface FutureTaskRequest {
  title: string;
  notes?: string | null;
  dueDate: string;
  dueTime?: string | null;
  category?: string | null;
  priority: TaskPriorityName;
  promoteToChecklistId?: string | null;
  reminders?: ReminderRequest[] | null;
}

export interface UpcomingGroups {
  today: FutureTask[];
  tomorrow: FutureTask[];
  thisWeek: FutureTask[];
  later: FutureTask[];
}

const BASE = '/api/v1/future-tasks';

@Injectable({ providedIn: 'root' })
export class FutureTasksApi {
  private readonly http = inject(HttpClient);

  list(status?: FutureTaskStatusName, from?: string, to?: string, q?: string): Observable<FutureTask[]> {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    if (q) params = params.set('q', q);
    return this.http.get<FutureTask[]>(BASE, { params });
  }

  upcoming(days = 30): Observable<UpcomingGroups> {
    const params = new HttpParams().set('days', days);
    return this.http.get<UpcomingGroups>(`${BASE}/upcoming`, { params });
  }

  create(request: FutureTaskRequest): Observable<FutureTask> {
    return this.http.post<FutureTask>(BASE, request);
  }

  update(id: string, request: FutureTaskRequest): Observable<FutureTask> {
    return this.http.put<FutureTask>(`${BASE}/${id}`, request);
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${id}`);
  }

  setStatus(id: string, status: FutureTaskStatusName): Observable<FutureTask> {
    return this.http.patch<FutureTask>(`${BASE}/${id}/status`, { status });
  }

  defer(id: string, newDueDate: string): Observable<FutureTask> {
    return this.http.patch<FutureTask>(`${BASE}/${id}/defer`, { newDueDate });
  }

  listReminders(taskId: string): Observable<FutureTaskReminder[]> {
    return this.http.get<FutureTaskReminder[]>(`${BASE}/${taskId}/reminders`);
  }

  createReminder(taskId: string, request: ReminderRequest): Observable<FutureTaskReminder> {
    return this.http.post<FutureTaskReminder>(`${BASE}/${taskId}/reminders`, request);
  }

  removeReminder(taskId: string, reminderId: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${taskId}/reminders/${reminderId}`);
  }
}
