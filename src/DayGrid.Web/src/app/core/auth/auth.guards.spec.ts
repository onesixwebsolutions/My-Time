import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';

import { FakeAuthService, fakeAuthService, makeUser, provideFakeAuth } from '../../testing/auth-testing';
import { adminGuard, authGuard, guestGuard } from './auth.guards';
import { isPublicAuthUrl, sanitizeReturnUrl } from './return-url';

describe('auth guards', () => {
  let auth: FakeAuthService;
  let router: Router;

  function setup(user = makeUser() as ReturnType<typeof makeUser> | null) {
    auth = fakeAuthService(user);
    TestBed.configureTestingModule({ providers: [provideRouter([]), provideFakeAuth(auth)] });
    router = TestBed.inject(Router);
  }

  function run(guard: typeof authGuard, url: string) {
    return TestBed.runInInjectionContext(() =>
      guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)
    );
  }

  function asUrl(result: unknown): string {
    expect(result instanceof UrlTree).withContext('expected a redirect').toBeTrue();
    return router.serializeUrl(result as UrlTree);
  }

  describe('authGuard', () => {
    it('lets signed-in users through', () => {
      setup();
      expect(run(authGuard, '/today')).toBeTrue();
    });

    it('sends signed-out users to /login with the requested url as returnUrl', () => {
      setup(null);
      expect(asUrl(run(authGuard, '/checklists/7?x=1'))).toBe('/login?returnUrl=%2Fchecklists%2F7%3Fx%3D1');
    });

    it('omits returnUrl for the root url', () => {
      setup(null);
      expect(asUrl(run(authGuard, '/'))).toBe('/login');
    });
  });

  describe('guestGuard', () => {
    it('lets signed-out users see auth pages', () => {
      setup(null);
      expect(run(guestGuard, '/login')).toBeTrue();
    });

    it('sends signed-in users home', () => {
      setup();
      expect(asUrl(run(guestGuard, '/login'))).toBe('/');
    });
  });

  describe('adminGuard', () => {
    it('lets admins through', () => {
      setup(makeUser({ roles: ['User', 'Admin'] }));
      expect(run(adminGuard, '/admin/users')).toBeTrue();
    });

    it('sends normal users home', () => {
      setup(makeUser({ roles: ['User'] }));
      expect(asUrl(run(adminGuard, '/admin/users'))).toBe('/');
    });

    it('sends signed-out users to login', () => {
      setup(null);
      expect(asUrl(run(adminGuard, '/admin/users'))).toBe('/login?returnUrl=%2Fadmin%2Fusers');
    });
  });
});

describe('sanitizeReturnUrl', () => {
  it('keeps same-origin relative paths with query and hash', () => {
    expect(sanitizeReturnUrl('/today')).toBe('/today');
    expect(sanitizeReturnUrl('/checklists/1?tab=a#b')).toBe('/checklists/1?tab=a#b');
  });

  it('rejects absolute, protocol-relative and backslash urls (open redirect)', () => {
    for (const bad of [
      'https://evil.example/',
      '//evil.example',
      '/\\evil.example',
      '\\\\evil.example',
      'javascript:alert(1)',
      'evil.example',
      '/\tevil',
      ''
    ]) {
      expect(sanitizeReturnUrl(bad)).withContext(bad).toBe('/');
    }
  });

  it('handles null/undefined and never bounces back to /login', () => {
    expect(sanitizeReturnUrl(null)).toBe('/');
    expect(sanitizeReturnUrl(undefined, '/today')).toBe('/today');
    expect(sanitizeReturnUrl('/login?returnUrl=/x')).toBe('/');
  });

  it('isPublicAuthUrl() recognises the auth pages', () => {
    expect(isPublicAuthUrl('/login?returnUrl=%2F')).toBeTrue();
    expect(isPublicAuthUrl('/register/check-email?email=a')).toBeTrue();
    expect(isPublicAuthUrl('/today')).toBeFalse();
  });
});
