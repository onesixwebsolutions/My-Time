import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Client for /api/v1/notifications — the in-app bell's data source. Backed by
// NotificationLog rows, which ReminderDispatcherService writes every time a
// scheduled Reminder becomes due (see ReminderDispatcherService.cs).

export type NotificationChannelName = 'InApp' | 'Email';

export interface NotificationLogEntry {
  id: string;
  reminderId: string | null;
  title: string;
  body: string;
  channel: NotificationChannelName;
  createdAtUtc: string;
  readAtUtc: string | null;
  error: string | null;
}

export interface TestNotificationResult {
  inAppSent: boolean;
  emailAttempted: boolean;
  emailError: string | null;
}

const BASE = '/api/v1/notifications';

@Injectable({ providedIn: 'root' })
export class NotificationsApi {
  private readonly http = inject(HttpClient);

  list(unreadOnly = false, take = 50): Observable<NotificationLogEntry[]> {
    const params = new HttpParams().set('unreadOnly', unreadOnly).set('take', take);
    return this.http.get<NotificationLogEntry[]>(BASE, { params });
  }

  markRead(id: string): Observable<NotificationLogEntry> {
    return this.http.patch<NotificationLogEntry>(`${BASE}/${id}/read`, null);
  }

  markAllRead(): Observable<void> {
    return this.http.patch<void>(`${BASE}/read-all`, null);
  }

  sendTest(): Observable<TestNotificationResult> {
    return this.http.post<TestNotificationResult>(`${BASE}/test`, {});
  }
}
