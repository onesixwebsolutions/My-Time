import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { FutureTaskRequest, FutureTasksApi } from './future-tasks.api';

describe('FutureTasksApi', () => {
  let api: FutureTasksApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(FutureTasksApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() without filters sends no params', () => {
    api.list().subscribe();
    const req = http.expectOne('/api/v1/future-tasks');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().length).toBe(0);
  });

  it('list() sends status/from/to/q', () => {
    api.list('Pending', '2026-10-01', '2026-10-31', 'dentist').subscribe();
    expect(http.expectOne('/api/v1/future-tasks?status=Pending&from=2026-10-01&to=2026-10-31&q=dentist').request.method).toBe('GET');
  });

  it('upcoming() defaults to 30 days', () => {
    api.upcoming().subscribe();
    expect(http.expectOne('/api/v1/future-tasks/upcoming?days=30').request.method).toBe('GET');
    api.upcoming(7).subscribe();
    http.expectOne('/api/v1/future-tasks/upcoming?days=7');
  });

  it('create() and update() send the body', () => {
    const body: FutureTaskRequest = {
      title: 'Dentist',
      dueDate: '2026-10-10',
      priority: 'High',
      reminders: [{ offsetMinutes: 60, channels: ['InApp', 'Email'] }]
    };
    api.create(body).subscribe();
    let req = http.expectOne('/api/v1/future-tasks');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);

    api.update('f1', body).subscribe();
    req = http.expectOne('/api/v1/future-tasks/f1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(body);
  });

  it('remove() DELETEs', () => {
    api.remove('f1').subscribe();
    expect(http.expectOne('/api/v1/future-tasks/f1').request.method).toBe('DELETE');
  });

  it('setStatus() PATCHes { status }', () => {
    api.setStatus('f1', 'Done').subscribe();
    const req = http.expectOne('/api/v1/future-tasks/f1/status');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ status: 'Done' });
  });

  it('defer() PATCHes { newDueDate }', () => {
    api.defer('f1', '2026-11-01').subscribe();
    const req = http.expectOne('/api/v1/future-tasks/f1/defer');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toEqual({ newDueDate: '2026-11-01' });
  });

  it('reminder sub-resource endpoints', () => {
    api.listReminders('f1').subscribe();
    expect(http.expectOne('/api/v1/future-tasks/f1/reminders').request.method).toBe('GET');

    const body = { offsetMinutes: 15, channels: ['InApp' as const] };
    api.createReminder('f1', body).subscribe();
    const req = http.expectOne((r) => r.method === 'POST' && r.url === '/api/v1/future-tasks/f1/reminders');
    expect(req.request.body).toEqual(body);

    api.removeReminder('f1', 'r1').subscribe();
    expect(http.expectOne('/api/v1/future-tasks/f1/reminders/r1').request.method).toBe('DELETE');
  });
});
