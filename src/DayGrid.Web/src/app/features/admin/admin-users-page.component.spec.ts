import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AdminApi } from '../../core/api/admin.api';
import { AdminUserDto } from '../../core/auth/auth.models';
import { fakeAuthService, makeUser, problem, provideFakeAuth } from '../../testing/auth-testing';
import { ADMIN_PAGE_SIZE, AdminUsersPageComponent } from './admin-users-page.component';

const ME = makeUser({ id: 'me', email: 'admin@example.com', roles: ['User', 'Admin'] });

function adminUser(overrides: Partial<AdminUserDto>): AdminUserDto {
  return {
    id: 'u',
    email: 'u@example.com',
    displayName: 'U',
    roles: ['User'],
    emailConfirmed: true,
    lockedOut: false,
    disabled: false,
    createdAt: '2026-01-01T00:00:00Z',
    lastLoginAt: null,
    ...overrides
  };
}

describe('AdminUsersPageComponent', () => {
  let fixture: ComponentFixture<AdminUsersPageComponent>;
  let el: HTMLElement;
  let api: jasmine.SpyObj<AdminApi>;

  const users = [
    adminUser({ id: 'me', email: 'admin@example.com', roles: ['User', 'Admin'] }),
    adminUser({ id: 'u2', email: 'bob@example.com', emailConfirmed: false }),
    adminUser({ id: 'u3', email: 'eve@example.com', lockedOut: true, disabled: true })
  ];

  beforeEach(() => {
    api = jasmine.createSpyObj<AdminApi>('AdminApi', ['listUsers', 'lock', 'unlock', 'resendConfirmation', 'deleteUser']);
    api.listUsers.and.returnValue(of({ items: users, total: 45 }));
    for (const m of ['lock', 'unlock', 'resendConfirmation', 'deleteUser'] as const) api[m].and.returnValue(of(void 0));
    TestBed.configureTestingModule({
      imports: [AdminUsersPageComponent],
      providers: [{ provide: AdminApi, useValue: api }, provideFakeAuth(fakeAuthService(ME))]
    });
    fixture = TestBed.createComponent(AdminUsersPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  const row = (id: string) => el.querySelector(`tr[data-user-id="${id}"]`) as HTMLElement;
  const btn = (scope: HTMLElement, label: string) =>
    Array.from(scope.querySelectorAll('button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

  it('loads the first page and shows the range', () => {
    expect(api.listUsers).toHaveBeenCalledWith('', 1, ADMIN_PAGE_SIZE);
    expect(el.textContent).toContain('bob@example.com');
    expect(el.querySelector('[data-testid="admin-range"]')?.textContent).toContain('1–3 of 45');
  });

  it('disables every action on the admin’s own row', () => {
    const own = row('me');
    expect(own.textContent).toContain('You');
    own.querySelectorAll('button').forEach((b) => expect(b.disabled).toBeTrue());
  });

  it('locks, unlocks and resends confirmation, then reloads', () => {
    btn(row('u2'), 'Lock').click();
    expect(api.lock).toHaveBeenCalledWith('u2');
    btn(row('u3'), 'Unlock').click();
    expect(api.unlock).toHaveBeenCalledWith('u3');
    fixture.detectChanges();
    btn(row('u2'), 'Resend').click();
    expect(api.resendConfirmation).toHaveBeenCalledWith('u2');
    fixture.detectChanges();
    expect(el.textContent).toContain('Confirmation email re-sent to bob@example.com.');
    expect(api.listUsers).toHaveBeenCalledTimes(4);
  });

  it('only offers Resend for unconfirmed users', () => {
    expect(btn(row('u2'), 'Resend')).toBeTruthy();
    expect(btn(row('u3'), 'Resend')).toBeUndefined();
  });

  it('asks for confirmation before deleting', () => {
    btn(row('u2'), 'Delete').click();
    fixture.detectChanges();
    expect(api.deleteUser).not.toHaveBeenCalled();
    const dialog = el.querySelector('[role="alertdialog"]') as HTMLElement;
    expect(dialog.textContent).toContain('Delete bob@example.com?');

    btn(dialog, 'Cancel').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alertdialog"]')).toBeNull();

    btn(row('u2'), 'Delete').click();
    fixture.detectChanges();
    btn(el.querySelector('[role="alertdialog"]') as HTMLElement, 'Delete account').click();
    fixture.detectChanges();
    expect(api.deleteUser).toHaveBeenCalledWith('u2');
    expect(el.querySelector('[role="alertdialog"]')).toBeNull();
    expect(el.textContent).toContain('Deleted bob@example.com.');
  });

  it('shows the server message when an action is refused', () => {
    api.lock.and.returnValue(throwError(() => problem(400, { title: 'Cannot lock this account.' })));
    btn(row('u2'), 'Lock').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Cannot lock this account.');
  });

  it('maps the cannot_* refusal codes to friendly messages', () => {
    api.lock.and.returnValue(throwError(() => problem(400, { code: 'cannot_lock_self', title: 'raw' })));
    btn(row('u2'), 'Lock').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('You can’t lock your own account.');

    api.deleteUser.and.returnValue(throwError(() => problem(400, { code: 'cannot_delete_self', title: 'raw' })));
    btn(row('u2'), 'Delete').click();
    fixture.detectChanges();
    btn(el.querySelector('[role="alertdialog"]') as HTMLElement, 'Delete account').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('use the Account page');
  });

  it('reports a vanished user (404) and lost admin rights (403)', () => {
    api.unlock.and.returnValue(throwError(() => problem(404)));
    btn(row('u3'), 'Unlock').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('That user no longer exists.');

    api.lock.and.returnValue(throwError(() => problem(403, { code: 'forbidden' })));
    btn(row('u2'), 'Lock').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('no longer have administrator access');
  });

  it('shows the rate-limit message with the Retry-After seconds', () => {
    api.lock.and.returnValue(throwError(() => problem(429, { code: 'rate_limited' }, { 'Retry-After': '7' })));
    btn(row('u2'), 'Lock').click();
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Too many attempts, try again in 7 s.');
  });

  it('pages forward and back', () => {
    btn(el, 'Next').click();
    expect(api.listUsers).toHaveBeenCalledWith('', 2, ADMIN_PAGE_SIZE);
    fixture.detectChanges();
    btn(el, 'Previous').click();
    expect(api.listUsers).toHaveBeenCalledWith('', 1, ADMIN_PAGE_SIZE);
  });

  it('debounces the search box and resets to page 1', fakeAsync(() => {
    btn(el, 'Next').click();
    const input = el.querySelector('#admin-search') as HTMLInputElement;
    input.value = 'bo';
    input.dispatchEvent(new Event('input'));
    input.value = 'bob';
    input.dispatchEvent(new Event('input'));
    tick(299);
    expect(api.listUsers).not.toHaveBeenCalledWith('bob', 1, ADMIN_PAGE_SIZE);
    tick(1);
    expect(api.listUsers).toHaveBeenCalledWith('bob', 1, ADMIN_PAGE_SIZE);
    expect(api.listUsers).not.toHaveBeenCalledWith('bo', 1, ADMIN_PAGE_SIZE);
  }));
});
