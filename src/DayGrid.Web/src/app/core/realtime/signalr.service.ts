import { Injectable, inject } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { Subject } from 'rxjs';

import { AuthService } from '../auth/auth.service';

// Matches plan section 5.7 — /hubs/schedule events.
export interface NowBlockChangedPayload {
  current: unknown;
  next: unknown;
}

export interface ReminderFiredPayload {
  id: string;
  title: string;
  body: string;
  channel: number;
}

export interface ItemCompletedPayload {
  itemId: string;
  date: string;
  status: number;
}

export interface PlanInvalidatedPayload {
  date: string;
}

const HUB_URL = '/hubs/schedule';
const MAX_RETRY_DELAY_MS = 30_000;

/** Exponential backoff capped at 30s; never gives up (returning null would stop reconnecting). */
export function retryDelayMs(attempt: number): number {
  return Math.min(MAX_RETRY_DELAY_MS, 1000 * 2 ** attempt);
}

@Injectable({ providedIn: 'root' })
export class SignalrService {
  private readonly auth = inject(AuthService);
  private connection: signalR.HubConnection | null = null;
  private restartTimer: ReturnType<typeof setTimeout> | null = null;
  private startAttempt = 0;

  private readonly nowBlockChangedSubject = new Subject<NowBlockChangedPayload>();
  private readonly reminderFiredSubject = new Subject<ReminderFiredPayload>();
  private readonly itemCompletedSubject = new Subject<ItemCompletedPayload>();
  private readonly planInvalidatedSubject = new Subject<PlanInvalidatedPayload>();

  readonly nowBlockChanged$ = this.nowBlockChangedSubject.asObservable();
  readonly reminderFired$ = this.reminderFiredSubject.asObservable();
  readonly itemCompleted$ = this.itemCompletedSubject.asObservable();
  readonly planInvalidated$ = this.planInvalidatedSubject.asObservable();

  constructor() {
    // The hub is [Authorize]: drop the connection as soon as the session ends (logout, account
    // deletion, or a 401 from the API). The auth cookie rides along automatically (same origin).
    this.auth.sessionEnded$.subscribe(() => this.disconnect());
  }

  /** Opens the hub connection — only while signed in (the hub would reject us with 401 otherwise). */
  connect(): void {
    if (this.connection || !this.auth.isAuthenticated()) return;

    // Relative URL: in production the SPA is served from the same origin as the API.
    // The default withAutomaticReconnect() gives up after 4 attempts (~42s) and does not
    // cover a failed initial start, so a short API restart would leave the hub dead
    // until a full page reload. Retry forever with capped backoff instead.
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL)
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => retryDelayMs(ctx.previousRetryCount) })
      .configureLogging(signalR.LogLevel.Warning)
      .build();
    this.connection = connection;

    connection.on('NowBlockChanged', (payload: NowBlockChangedPayload) =>
      this.nowBlockChangedSubject.next(payload)
    );
    connection.on('ReminderFired', (payload: ReminderFiredPayload) =>
      this.reminderFiredSubject.next(payload)
    );
    connection.on('ItemCompleted', (payload: ItemCompletedPayload) =>
      this.itemCompletedSubject.next(payload)
    );
    connection.on('PlanInvalidated', (payload: PlanInvalidatedPayload) =>
      this.planInvalidatedSubject.next(payload)
    );
    // Fires on an intentional stop() too — scheduleRestart() ignores that case because
    // disconnect() clears `this.connection` first.
    connection.onclose(() => this.scheduleRestart(connection));

    this.start(connection);
  }

  disconnect(): void {
    if (this.restartTimer) clearTimeout(this.restartTimer);
    this.restartTimer = null;
    this.startAttempt = 0;
    const connection = this.connection;
    this.connection = null;
    connection?.stop().catch(() => void 0);
  }

  private start(connection: signalR.HubConnection): void {
    if (this.connection !== connection) return;
    connection
      .start()
      .then(() => (this.startAttempt = 0))
      .catch((err) => {
        console.error('SignalR connection failed', err);
        this.scheduleRestart(connection);
      });
  }

  private scheduleRestart(connection: signalR.HubConnection): void {
    if (this.connection !== connection || this.restartTimer) return;
    const delay = retryDelayMs(this.startAttempt++);
    this.restartTimer = setTimeout(() => {
      this.restartTimer = null;
      this.start(connection);
    }, delay);
  }
}
