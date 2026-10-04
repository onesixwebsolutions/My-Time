import { HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { Provider, computed, signal } from '@angular/core';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { Subject, of } from 'rxjs';

import { UserDto } from '../core/auth/auth.models';
import { AuthService } from '../core/auth/auth.service';
import { EmailLinkService } from '../core/auth/email-link.service';

export function makeUser(overrides: Partial<UserDto> = {}): UserDto {
  return {
    id: '11111111-1111-1111-1111-111111111111',
    email: 'ada@example.com',
    displayName: 'Ada Lovelace',
    timeZone: 'Europe/London',
    roles: ['User'],
    emailConfirmed: true,
    createdAt: '2026-01-01T00:00:00Z',
    ...overrides
  };
}

/** Lightweight AuthService stand-in for components that only read the user / call logout. */
export function fakeAuthService(user: UserDto | null = makeUser()) {
  const current = signal<UserDto | null>(user);
  const sessionEnded = new Subject<void>();
  const fake = {
    currentUser: current.asReadonly(),
    isAuthenticated: computed(() => current() !== null),
    isAdmin: computed(() => current()?.roles.includes('Admin') ?? false),
    sessionEnded$: sessionEnded.asObservable(),
    logout: jasmine.createSpy('logout').and.callFake(() => {
      current.set(null);
      sessionEnded.next();
      return of(void 0);
    }),
    clearSession: jasmine.createSpy('clearSession').and.callFake(() => {
      current.set(null);
      sessionEnded.next();
    }),
    /** Test hook: change the signed-in user. */
    setUser: (u: UserDto | null) => current.set(u),
    endSession: () => sessionEnded.next()
  };
  return fake;
}

export type FakeAuthService = ReturnType<typeof fakeAuthService>;

export function provideFakeAuth(fake: FakeAuthService): Provider {
  return { provide: AuthService, useValue: fake };
}

/** ActivatedRoute stub exposing only snapshot.queryParamMap (what the auth pages read). */
export function routeWithQuery(params: Record<string, string>): Provider {
  return {
    provide: ActivatedRoute,
    useValue: { snapshot: { queryParamMap: convertToParamMap(params), paramMap: convertToParamMap({}) } }
  };
}

/** EmailLinkService stand-in: the parameters of the emailed link (fragment/query) the page reads. */
export function emailLinkWith(params: Record<string, string>) {
  const take = jasmine.createSpy('take').and.callFake((names: readonly string[]) =>
    Object.fromEntries(names.map((n) => [n, params[n] ?? null]))
  );
  return { take };
}

export function provideEmailLink(fake: ReturnType<typeof emailLinkWith>): Provider {
  return { provide: EmailLinkService, useValue: fake };
}

export function problem(status: number, body: Record<string, unknown> = {}, headers: Record<string, string> = {}) {
  return new HttpErrorResponse({ status, error: body, headers: new HttpHeaders(headers) });
}

/** Sets an input's value and fires the events reactive forms listen to. */
export function typeInto(root: HTMLElement, selector: string, value: string): void {
  const input = root.querySelector(selector) as HTMLInputElement;
  if (!input) throw new Error(`No element for ${selector}`);
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('blur'));
}

export function submitForm(root: HTMLElement, selector = 'form'): void {
  const form = root.querySelector(selector) as HTMLFormElement;
  form.dispatchEvent(new Event('submit'));
}
