import { Route } from '@angular/router';

import { adminGuard, authGuard, guestGuard } from './core/auth/auth.guards';
import { routes } from './app.routes';

function find(path: string, list: Route[] = routes, parents: Route[] = []): { route: Route; parents: Route[] } | null {
  for (const r of list) {
    if (r.path === path && !r.children) return { route: r, parents };
    if (r.children) {
      const hit = find(path, r.children, [...parents, r]);
      if (hit) return hit;
    }
  }
  return null;
}

describe('app routes', () => {
  it('puts the public auth pages outside the authenticated shell, with guestGuard where the contract says', () => {
    for (const path of ['login', 'register', 'register/check-email', 'forgot-password']) {
      const hit = find(path)!;
      expect(hit).withContext(path).toBeTruthy();
      expect(hit.route.canActivate).withContext(path).toContain(guestGuard);
      expect(hit.parents.some((p) => p.canActivate?.includes(authGuard))).withContext(path).toBeFalse();
    }
    for (const path of ['confirm-email', 'reset-password']) {
      const hit = find(path)!;
      expect(hit.parents.some((p) => p.canActivate?.includes(authGuard))).withContext(path).toBeFalse();
    }
  });

  it('requires sign-in for app pages and the admin role for /admin/users', () => {
    for (const path of ['today', 'checklists', 'settings', 'account', 'admin/users']) {
      const hit = find(path)!;
      expect(hit.parents.some((p) => p.canActivate?.includes(authGuard))).withContext(path).toBeTrue();
    }
    expect(find('admin/users')!.route.canActivate).toContain(adminGuard);
  });
});
