import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { makeUser } from '../../testing/auth-testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let auth: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    auth = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  async function signIn(user = makeUser()) {
    const p = auth.loadCurrentUser();
    http.expectOne('/api/v1/auth/me').flush(user);
    await p;
  }

  it('starts signed out', () => {
    expect(auth.currentUser()).toBeNull();
    expect(auth.isAuthenticated()).toBeFalse();
    expect(auth.isAdmin()).toBeFalse();
  });

  it('loadCurrentUser() GETs /auth/me and stores the user', async () => {
    const p = auth.loadCurrentUser();
    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('GET');
    req.flush(makeUser({ roles: ['User', 'Admin'] }));
    expect((await p)?.email).toBe('ada@example.com');
    expect(auth.isAuthenticated()).toBeTrue();
    expect(auth.isAdmin()).toBeTrue();
  });

  it('loadCurrentUser() resolves null on 401 and never throws', async () => {
    const p = auth.loadCurrentUser();
    http.expectOne('/api/v1/auth/me').flush(null, { status: 401, statusText: 'Unauthorized' });
    expect(await p).toBeNull();
    expect(auth.currentUser()).toBeNull();
  });

  it('loadCurrentUser() resolves null on network/server errors too', async () => {
    const p = auth.loadCurrentUser();
    http.expectOne('/api/v1/auth/me').error(new ProgressEvent('error'));
    expect(await p).toBeNull();
  });

  it('register() POSTs { email, password, displayName }', () => {
    let done = false;
    auth.register({ email: 'a@b.co', password: 'pw-1234567890', displayName: 'A' }).subscribe(() => (done = true));
    const req = http.expectOne('/api/v1/auth/register');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.co', password: 'pw-1234567890', displayName: 'A' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    expect(done).toBeTrue();
  });

  it('confirmEmail() POSTs { userId, token, password }', () => {
    auth.confirmEmail('u1', 'tok', 'pw').subscribe();
    const req = http.expectOne('/api/v1/auth/confirm-email');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ userId: 'u1', token: 'tok', password: 'pw' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('resendConfirmation() POSTs { email }', () => {
    auth.resendConfirmation('a@b.co').subscribe();
    const req = http.expectOne('/api/v1/auth/resend-confirmation');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.co' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
  });

  it('login() POSTs credentials and stores the returned user', () => {
    auth.login({ email: 'a@b.co', password: 'secret-secret', rememberMe: true }).subscribe();
    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.co', password: 'secret-secret', rememberMe: true });
    req.flush(makeUser());
    expect(auth.currentUser()?.id).toBe(makeUser().id);
  });

  it('login() failure leaves the user signed out', () => {
    let failed = false;
    auth.login({ email: 'a@b.co', password: 'x', rememberMe: false }).subscribe({ error: () => (failed = true) });
    http.expectOne('/api/v1/auth/login').flush({ code: 'invalid_credentials' }, { status: 401, statusText: 'Unauthorized' });
    expect(failed).toBeTrue();
    expect(auth.isAuthenticated()).toBeFalse();
  });

  it('logout() POSTs and clears the user (and emits sessionEnded$)', async () => {
    await signIn();
    let ended = 0;
    auth.sessionEnded$.subscribe(() => ended++);
    auth.logout().subscribe();
    const req = http.expectOne('/api/v1/auth/logout');
    expect(req.request.method).toBe('POST');
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.currentUser()).toBeNull();
    expect(ended).toBe(1);
  });

  it('logout() clears the user even when the request fails', async () => {
    await signIn();
    auth.logout().subscribe({ error: () => void 0 });
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 500, statusText: 'Error' });
    expect(auth.currentUser()).toBeNull();
  });

  it('forgotPassword() POSTs { email }', () => {
    auth.forgotPassword('a@b.co').subscribe();
    const req = http.expectOne('/api/v1/auth/forgot-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.co' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
  });

  it('resetPassword() POSTs { email, token, newPassword }', () => {
    auth.resetPassword({ email: 'a@b.co', token: 't', newPassword: 'new-password-1' }).subscribe();
    const req = http.expectOne('/api/v1/auth/reset-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ email: 'a@b.co', token: 't', newPassword: 'new-password-1' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('changePassword() POSTs { currentPassword, newPassword }', () => {
    auth.changePassword({ currentPassword: 'old-password', newPassword: 'new-password-1' }).subscribe();
    const req = http.expectOne('/api/v1/auth/change-password');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ currentPassword: 'old-password', newPassword: 'new-password-1' });
    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('updateProfile() PUTs /auth/me and stores the updated user', async () => {
    await signIn();
    auth.updateProfile({ displayName: 'Ada L', timeZone: 'Asia/Kolkata' }).subscribe();
    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ displayName: 'Ada L', timeZone: 'Asia/Kolkata' });
    req.flush(makeUser({ displayName: 'Ada L', timeZone: 'Asia/Kolkata' }));
    expect(auth.currentUser()?.timeZone).toBe('Asia/Kolkata');
  });

  it('deleteAccount() DELETEs /auth/me with { password } and clears the user', async () => {
    await signIn();
    auth.deleteAccount('my-password').subscribe();
    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('DELETE');
    expect(req.request.body).toEqual({ password: 'my-password' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.currentUser()).toBeNull();
  });

  it('deleteAccount() keeps the user when the password is wrong', async () => {
    await signIn();
    auth.deleteAccount('bad').subscribe({ error: () => void 0 });
    http.expectOne('/api/v1/auth/me').flush({ code: 'invalid_credentials' }, { status: 400, statusText: 'Bad Request' });
    expect(auth.isAuthenticated()).toBeTrue();
  });

  it('clearSession() only emits sessionEnded$ when someone was signed in', async () => {
    let ended = 0;
    auth.sessionEnded$.subscribe(() => ended++);
    auth.clearSession();
    expect(ended).toBe(0);
    await signIn();
    auth.clearSession();
    expect(ended).toBe(1);
  });
});
