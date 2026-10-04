import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { NotificationsApi } from './notifications.api';

describe('NotificationsApi', () => {
  let api: NotificationsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(NotificationsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('list() defaults to unreadOnly=false&take=50', () => {
    api.list().subscribe();
    expect(http.expectOne('/api/v1/notifications?unreadOnly=false&take=50').request.method).toBe('GET');
  });

  it('list() forwards custom args', () => {
    api.list(true, 100).subscribe();
    expect(http.expectOne('/api/v1/notifications?unreadOnly=true&take=100').request.method).toBe('GET');
  });

  it('markRead() PATCHes /{id}/read with null body', () => {
    api.markRead('n1').subscribe();
    const req = http.expectOne('/api/v1/notifications/n1/read');
    expect(req.request.method).toBe('PATCH');
    expect(req.request.body).toBeNull();
  });

  it('markAllRead() PATCHes /read-all', () => {
    api.markAllRead().subscribe();
    expect(http.expectOne('/api/v1/notifications/read-all').request.method).toBe('PATCH');
  });

  it('sendTest() POSTs an empty object and returns the result', () => {
    let result: unknown;
    api.sendTest().subscribe((r) => (result = r));
    const req = http.expectOne('/api/v1/notifications/test');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    const payload = { inAppSent: true, emailAttempted: false, emailError: null };
    req.flush(payload);
    expect(result).toEqual(payload);
  });
});
