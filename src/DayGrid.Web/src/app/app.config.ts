import { ApplicationConfig, inject, provideAppInitializer, provideZoneChangeDetection } from '@angular/core';
import { provideHttpClient, withFetch, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
import { XSRF_COOKIE_NAME, XSRF_HEADER_NAME, authInterceptor } from './core/auth/auth.interceptor';
import { AuthService } from './core/auth/auth.service';

/**
 * Resolve the signed-in user (GET /api/v1/auth/me) before the first navigation so the route
 * guards can decide synchronously. The call also makes the server issue the XSRF-TOKEN cookie.
 * loadCurrentUser() never rejects — a 401 just means "signed out".
 */
export function initAuth(): Promise<unknown> {
  return inject(AuthService).loadCurrentUser();
}

export const appConfig: ApplicationConfig = {
  providers: [
    // Angular 21+ defaults to zoneless; DayGrid keeps zone.js-based change detection.
    provideZoneChangeDetection(),
    provideRouter(routes),
    provideHttpClient(
      withFetch(),
      withInterceptors([authInterceptor]),
      // Angular's XSRF interceptor only touches unsafe methods on relative URLs — all API URLs are relative.
      withXsrfConfiguration({ cookieName: XSRF_COOKIE_NAME, headerName: XSRF_HEADER_NAME })
    ),
    provideAppInitializer(initAuth)
  ]
};
