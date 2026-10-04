import { TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import * as signalR from '@microsoft/signalr';

import { FakeAuthService, fakeAuthService, provideFakeAuth } from '../../testing/auth-testing';
import { ReminderFiredPayload, SignalrService, retryDelayMs } from './signalr.service';

type Handler = (...args: unknown[]) => void;

/** Minimal stand-in for HubConnection — no network. */
class FakeHubConnection {
  readonly handlers = new Map<string, Handler>();
  closeHandler: ((err?: Error) => void) | null = null;
  start = jasmine.createSpy('start').and.returnValue(Promise.resolve());
  stop = jasmine.createSpy('stop').and.returnValue(Promise.resolve());

  on(event: string, handler: Handler): void {
    this.handlers.set(event, handler);
  }

  onclose(handler: (err?: Error) => void): void {
    this.closeHandler = handler;
  }
}

describe('SignalrService', () => {
  let service: SignalrService;
  let fake: FakeHubConnection;
  let withUrlSpy: jasmine.Spy;
  let reconnectSpy: jasmine.Spy;
  let buildSpy: jasmine.Spy;
  let auth: FakeAuthService;

  beforeEach(() => {
    fake = new FakeHubConnection();
    const proto = signalR.HubConnectionBuilder.prototype;
    withUrlSpy = spyOn(proto, 'withUrl').and.callThrough();
    reconnectSpy = spyOn(proto, 'withAutomaticReconnect').and.callThrough();
    buildSpy = spyOn(proto, 'build').and.returnValue(fake as unknown as signalR.HubConnection);
    spyOn(console, 'error');

    auth = fakeAuthService();
    TestBed.configureTestingModule({ providers: [provideFakeAuth(auth)] });
    service = TestBed.inject(SignalrService);
  });

  it('does not connect while signed out', () => {
    auth.setUser(null);
    service.connect();
    expect(buildSpy).not.toHaveBeenCalled();
  });

  it('stops the connection when the session ends (logout / 401)', () => {
    service.connect();
    auth.endSession();
    expect(fake.stop).toHaveBeenCalled();
    service.connect();
    expect(buildSpy).toHaveBeenCalledTimes(2);
  });

  it('retryDelayMs() backs off exponentially and caps at 30s', () => {
    expect(retryDelayMs(0)).toBe(1000);
    expect(retryDelayMs(1)).toBe(2000);
    expect(retryDelayMs(3)).toBe(8000);
    expect(retryDelayMs(10)).toBe(30000);
  });

  it('connects to the relative same-origin hub URL', () => {
    service.connect();
    expect(withUrlSpy).toHaveBeenCalledWith('/hubs/schedule');
    expect(fake.start).toHaveBeenCalledTimes(1);
  });

  it('configures an automatic reconnect policy that never gives up', () => {
    service.connect();
    const policy = reconnectSpy.calls.mostRecent().args[0] as signalR.IRetryPolicy;
    const ctx = { previousRetryCount: 50, elapsedMilliseconds: 1e9, retryReason: new Error() };
    expect(policy.nextRetryDelayInMilliseconds(ctx)).toBe(30000);
  });

  it('is idempotent while connected', () => {
    service.connect();
    service.connect();
    expect(buildSpy).toHaveBeenCalledTimes(1);
  });

  it('forwards hub events to the matching observables', () => {
    const received: ReminderFiredPayload[] = [];
    const dates: string[] = [];
    service.reminderFired$.subscribe((p) => received.push(p));
    service.planInvalidated$.subscribe((p) => dates.push(p.date));
    service.connect();

    const payload: ReminderFiredPayload = { id: 'r1', title: 'Stand up', body: '', channel: 1 };
    fake.handlers.get('ReminderFired')!(payload);
    fake.handlers.get('PlanInvalidated')!({ date: '2026-10-04' });

    expect(received).toEqual([payload]);
    expect(dates).toEqual(['2026-10-04']);
    expect([...fake.handlers.keys()].sort()).toEqual(['ItemCompleted', 'NowBlockChanged', 'PlanInvalidated', 'ReminderFired']);
  });

  it('retries the initial start with backoff when it fails', fakeAsync(() => {
    fake.start.and.returnValues(Promise.reject(new Error('down')), Promise.resolve());

    service.connect();
    flushMicrotasks();
    expect(fake.start).toHaveBeenCalledTimes(1);

    tick(retryDelayMs(0));
    flushMicrotasks();
    expect(fake.start).toHaveBeenCalledTimes(2);
  }));

  it('restarts after the connection closes unexpectedly', fakeAsync(() => {
    service.connect();
    flushMicrotasks();

    fake.closeHandler!(new Error('lost'));
    tick(retryDelayMs(0));
    flushMicrotasks();

    expect(fake.start).toHaveBeenCalledTimes(2);
  }));

  it('disconnect() stops the connection and cancels pending restarts', fakeAsync(() => {
    fake.start.and.returnValue(Promise.reject(new Error('down')));
    service.connect();
    flushMicrotasks();

    service.disconnect();
    fake.closeHandler!();
    tick(60000);
    flushMicrotasks();

    expect(fake.stop).toHaveBeenCalled();
    expect(fake.start).toHaveBeenCalledTimes(1);
  }));

  it('can reconnect after disconnect()', () => {
    service.connect();
    service.disconnect();
    service.connect();
    expect(buildSpy).toHaveBeenCalledTimes(2);
  });
});
