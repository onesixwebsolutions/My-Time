import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AdminApi } from './admin.api';

describe('AdminApi', () => {
  let api: AdminApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AdminApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('listUsers() GETs /admin/users with search, page and pageSize', () => {
    api.listUsers('ada', 2, 20).subscribe();
    const req = http.expectOne((r) => r.url === '/api/v1/admin/users');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('search')).toBe('ada');
    expect(req.request.params.get('page')).toBe('2');
    expect(req.request.params.get('pageSize')).toBe('20');
    req.flush({ items: [], total: 0 });
  });

  it('lock/unlock/resend POST to the per-user action urls', () => {
    api.lock('u1').subscribe();
    api.unlock('u1').subscribe();
    api.resendConfirmation('u1').subscribe();
    for (const action of ['lock', 'unlock', 'resend-confirmation']) {
      const req = http.expectOne(`/api/v1/admin/users/u1/${action}`);
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
    }
  });

  it('deleteUser() DELETEs /admin/users/{id}', () => {
    api.deleteUser('u1').subscribe();
    const req = http.expectOne('/api/v1/admin/users/u1');
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
