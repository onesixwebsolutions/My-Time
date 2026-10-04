import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';

import { AuthService } from './auth.service';
import { sanitizeReturnUrl } from './return-url';

// The user is resolved by the app initializer (GET /auth/me) before the first navigation, so the
// guards can read the signal synchronously.

function loginRedirect(router: Router, url: string): UrlTree {
  const returnUrl = sanitizeReturnUrl(url, '');
  return router.createUrlTree(['/login'], {
    queryParams: returnUrl && returnUrl !== '/' ? { returnUrl } : {}
  });
}

/** Signed-in users only; everyone else goes to /login?returnUrl=<requested url>. */
export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? true : loginRedirect(inject(Router), state.url);
};

/** Auth pages (login/register/...) are skipped by users who are already signed in. */
export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isAuthenticated() ? inject(Router).createUrlTree(['/']) : true;
};

/** Admin role only; signed-out users go to login, other users to the home page. */
export const adminGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  if (!auth.isAuthenticated()) return loginRedirect(router, state.url);
  return auth.isAdmin() ? true : router.createUrlTree(['/']);
};
