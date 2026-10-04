import { HttpClient, HttpXsrfTokenExtractor, provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { makeUser } from '../../testing/auth-testing';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let http: HttpClient;
  let ctrl: HttpTestingController;
  let auth: AuthService;
  let router: Router;
  let navigate: jasmine.Spy;
  let currentUrl: string;
  let xsrfToken: string | null;

  beforeEach(async () => {
    xsrfToken = 'token-1';
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(
          withInterceptors([authInterceptor]),
          withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })
        ),
        provideHttpClientTesting(),
        { provide: HttpXsrfTokenExtractor, useValue: { getToken: () => xsrfToken } }
      ]
    });
    http = TestBed.inject(HttpClient);
    ctrl = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    router = TestBed.inject(Router);
    currentUrl = '/checklists/42?tab=items';
    spyOnProperty(router, 'url', 'get').and.callFake(() => currentUrl);
    navigate = spyOn(router, 'navigate').and.resolveTo(true);

    // Signed in to start with.
    const p = auth.loadCurrentUser();
    ctrl.expectOne('/api/v1/auth/me').flush(makeUser());
    await p;
  });

  afterEach(() => ctrl.verify());

  it('on 401 clears the user and redirects to /login with the current url as returnUrl', () => {
    let status = 0;
    http.get('/api/v1/checklists').subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/v1/checklists').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(status).toBe(401);
    expect(auth.currentUser()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/checklists/42?tab=items' } });
  });

  it('does not redirect for 401 from GET /auth/me', () => {
    http.get('/api/v1/auth/me').subscribe({ error: () => void 0 });
    ctrl.expectOne('/api/v1/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).not.toHaveBeenCalled();
  });

  it('does not redirect for 401 from POST /auth/login (wrong password)', () => {
    http.post('/api/v1/auth/login', {}).subscribe({ error: () => void 0 });
    ctrl.expectOne('/api/v1/auth/login').flush({ code: 'invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).not.toHaveBeenCalled();
    expect(auth.isAuthenticated()).toBeTrue();
  });

  it('does not add a returnUrl when already on a public auth page', () => {
    currentUrl = '/confirm-email?userId=1&token=x';
    http.get('/api/v1/notifications').subscribe({ error: () => void 0 });
    ctrl.expectOne('/api/v1/notifications').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).not.toHaveBeenCalled();
  });

  it('passes other errors straight through', () => {
    let status = 0;
    http.get('/api/v1/tasks').subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/v1/tasks').flush(null, { status: 403, statusText: 'Forbidden' });
    expect(status).toBe(403);
    expect(navigate).not.toHaveBeenCalled();
    expect(auth.isAuthenticated()).toBeTrue();
  });

  it('on 400 antiforgery re-fetches /auth/me and retries once with the fresh token', () => {
    let result: unknown;
    http.post('/api/v1/tasks', { title: 'x' }).subscribe((r) => (result = r));

    ctrl.expectOne('/api/v1/tasks').flush({ code: 'antiforgery' }, { status: 400, statusText: 'Bad Request' });
    xsrfToken = 'token-2';
    const me = ctrl.expectOne('/api/v1/auth/me');
    expect(me.request.method).toBe('GET');
    me.flush(makeUser());

    const retry = ctrl.expectOne('/api/v1/tasks');
    expect(retry.request.method).toBe('POST');
    expect(retry.request.body).toEqual({ title: 'x' });
    expect(retry.request.headers.get('X-XSRF-TOKEN')).toBe('token-2');
    retry.flush({ id: 't1' });
    expect(result).toEqual({ id: 't1' });
  });

  it('retries an antiforgery failure only once', () => {
    let status = 0;
    http.post('/api/v1/tasks', {}).subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/v1/tasks').flush({ code: 'antiforgery' }, { status: 400, statusText: 'Bad Request' });
    ctrl.expectOne('/api/v1/auth/me').flush(makeUser());
    ctrl.expectOne('/api/v1/tasks').flush({ code: 'antiforgery' }, { status: 400, statusText: 'Bad Request' });

    ctrl.expectNone('/api/v1/auth/me');
    expect(status).toBe(400);
  });

  it('does not retry other 400s', () => {
    let status = 0;
    http.post('/api/v1/tasks', {}).subscribe({ error: (e) => (status = e.status) });
    ctrl.expectOne('/api/v1/tasks').flush({ errors: { title: ['Required'] } }, { status: 400, statusText: 'Bad Request' });
    ctrl.expectNone('/api/v1/auth/me');
    expect(status).toBe(400);
  });

  it('redirects to login if the antiforgery retry comes back 401', () => {
    http.post('/api/v1/tasks', {}).subscribe({ error: () => void 0 });
    ctrl.expectOne('/api/v1/tasks').flush({ code: 'antiforgery' }, { status: 400, statusText: 'Bad Request' });
    ctrl.expectOne('/api/v1/auth/me').flush(makeUser());
    ctrl.expectOne('/api/v1/tasks').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).toHaveBeenCalled();
  });
});
