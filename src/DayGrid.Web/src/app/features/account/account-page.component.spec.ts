import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { makeUser, submitForm, typeInto } from '../../testing/auth-testing';
import { AccountPageComponent, FALLBACK_TIME_ZONES, LAST_ADMIN_MESSAGE, availableTimeZones } from './account-page.component';

describe('AccountPageComponent', () => {
  let fixture: ComponentFixture<AccountPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let auth: AuthService;
  let navigate: jasmine.Spy;
  let navigateByUrl: jasmine.Spy;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [AccountPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()]
    });
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    const router = TestBed.inject(Router);
    navigate = spyOn(router, 'navigate').and.resolveTo(true);
    navigateByUrl = spyOn(router, 'navigateByUrl').and.resolveTo(true);

    const p = auth.loadCurrentUser();
    http.expectOne('/api/v1/auth/me').flush(makeUser({ timeZone: 'Asia/Kolkata' }));
    await p;

    fixture = TestBed.createComponent(AccountPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  const forms = () => el.querySelectorAll('form');

  it('prefills the profile from the current user', () => {
    expect(el.textContent).toContain('ada@example.com');
    expect((el.querySelector('#account-name') as HTMLInputElement).value).toBe('Ada Lovelace');
    expect((el.querySelector('#account-timezone') as HTMLSelectElement).value).toBe('Asia/Kolkata');
  });

  it('saves the profile with PUT /auth/me', () => {
    typeInto(el, '#account-name', 'Ada L');
    submitForm(el, 'form');
    const req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ displayName: 'Ada L', timeZone: 'Asia/Kolkata' });
    req.flush(makeUser({ displayName: 'Ada L', timeZone: 'Asia/Kolkata' }));
    fixture.detectChanges();
    expect(el.textContent).toContain('Profile saved.');
    expect(auth.currentUser()?.displayName).toBe('Ada L');
  });

  it('maps a server timeZone error onto the select', () => {
    submitForm(el, 'form');
    http.expectOne('/api/v1/auth/me').flush({ errors: { timeZone: ['Unknown time zone.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('#account-timezone-error')?.textContent).toContain('Unknown time zone.');
  });

  it('changes the password and maps invalid_credentials onto the current password', () => {
    typeInto(el, '#account-current-password', 'old-password-1');
    typeInto(el, '#account-new-password', 'new passphrase 1');
    typeInto(el, '#account-confirm-password', 'new passphrase 1');
    forms()[1].dispatchEvent(new Event('submit'));
    let req = http.expectOne('/api/v1/auth/change-password');
    expect(req.request.body).toEqual({ currentPassword: 'old-password-1', newPassword: 'new passphrase 1' });
    req.flush({ code: 'invalid_credentials' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('#account-current-password-error')?.textContent).toContain('Current password is incorrect.');

    typeInto(el, '#account-current-password', 'right-password-1');
    forms()[1].dispatchEvent(new Event('submit'));
    req = http.expectOne('/api/v1/auth/change-password');
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Password updated.');
  });

  it('validates the new password length and confirmation client-side', () => {
    typeInto(el, '#account-current-password', 'old-password-1');
    typeInto(el, '#account-new-password', 'short');
    typeInto(el, '#account-confirm-password', 'other');
    forms()[1].dispatchEvent(new Event('submit'));
    fixture.detectChanges();
    expect(el.textContent).toContain('New password must be at least 10 characters.');
    expect(el.textContent).toContain('Passwords do not match.');
    http.expectNone('/api/v1/auth/change-password');
  });

  it('deletes the account after password confirmation in the dialog', () => {
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Delete account…')) as HTMLButtonElement).click();
    fixture.detectChanges();
    const dialog = el.querySelector('[role="alertdialog"]') as HTMLElement;
    expect(dialog).toBeTruthy();

    typeInto(el, '#account-delete-password', 'wrong');
    dialog.querySelector('form')!.dispatchEvent(new Event('submit'));
    let req = http.expectOne('/api/v1/auth/me');
    expect(req.request.method).toBe('DELETE');
    req.flush({ code: 'invalid_credentials' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('#account-delete-password-error')?.textContent).toContain('Incorrect password.');

    typeInto(el, '#account-delete-password', 'right-password');
    el.querySelector('[role="alertdialog"] form')!.dispatchEvent(new Event('submit'));
    req = http.expectOne('/api/v1/auth/me');
    expect(req.request.body).toEqual({ password: 'right-password' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.currentUser()).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { deleted: 1 } });
  });

  it('explains last_admin when the only administrator tries to delete their account', () => {
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Delete account…')) as HTMLButtonElement).click();
    fixture.detectChanges();
    typeInto(el, '#account-delete-password', 'right-password');
    el.querySelector('[role="alertdialog"] form')!.dispatchEvent(new Event('submit'));
    http.expectOne('/api/v1/auth/me').flush({ status: 400, code: 'last_admin' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    const dialog = el.querySelector('[role="alertdialog"]') as HTMLElement;
    expect(dialog).toBeTruthy();
    expect(dialog.querySelector('[role="alert"]')?.textContent).toContain(LAST_ADMIN_MESSAGE);
    expect(auth.currentUser()).not.toBeNull();
    expect(navigate).not.toHaveBeenCalled();
  });

  it('maps a newPassword validation error and shows the rate-limit message', () => {
    typeInto(el, '#account-current-password', 'old-password-1');
    typeInto(el, '#account-new-password', 'new passphrase 1');
    typeInto(el, '#account-confirm-password', 'new passphrase 1');
    forms()[1].dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/v1/auth/change-password')
      .flush({ code: 'validation', errors: { newPassword: ['Too common.'] } }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('#account-new-password-error')?.textContent).toContain('Too common.');

    typeInto(el, '#account-new-password', 'new passphrase 2');
    typeInto(el, '#account-confirm-password', 'new passphrase 2');
    forms()[1].dispatchEvent(new Event('submit'));
    http
      .expectOne('/api/v1/auth/change-password')
      .flush({ status: 429, code: 'rate_limited' }, { status: 429, statusText: 'Too Many Requests', headers: { 'Retry-After': '42' } });
    fixture.detectChanges();
    expect(el.textContent).toContain('Too many attempts, try again in 42 s.');
  });

  it('signs out and goes to /login', () => {
    (Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Sign out') as HTMLButtonElement).click();
    http.expectOne('/api/v1/auth/logout').flush(null, { status: 204, statusText: 'No Content' });
    expect(auth.currentUser()).toBeNull();
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });
});

describe('availableTimeZones', () => {
  it('includes the current zone and UTC, sorted', () => {
    const zones = availableTimeZones('Etc/Custom');
    expect(zones).toContain('Etc/Custom');
    expect(zones).toContain('UTC');
    expect([...zones].sort((a, b) => a.localeCompare(b))).toEqual(zones);
  });

  it('falls back to a built-in list when Intl.supportedValuesOf is unavailable', () => {
    const intl = Intl as unknown as { supportedValuesOf?: unknown };
    const original = intl.supportedValuesOf;
    intl.supportedValuesOf = undefined;
    try {
      expect(availableTimeZones(null).length).toBe(FALLBACK_TIME_ZONES.length);
    } finally {
      intl.supportedValuesOf = original;
    }
  });
});
