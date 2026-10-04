import {
  HttpContextToken,
  HttpErrorResponse,
  HttpEvent,
  HttpInterceptorFn,
  HttpRequest,
  HttpXsrfTokenExtractor
} from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { Observable, catchError, switchMap, throwError } from 'rxjs';

import { AUTH_LOGIN_URL, AUTH_ME_URL, AuthService } from './auth.service';
import { isPublicAuthUrl } from './return-url';

export const XSRF_HEADER_NAME = 'X-XSRF-TOKEN';
export const XSRF_COOKIE_NAME = 'XSRF-TOKEN';

/** Marks a request that has already been retried after an antiforgery failure. */
const ANTIFORGERY_RETRIED = new HttpContextToken<boolean>(() => false);

function pathOf(url: string): string {
  return url.split(/[?#]/)[0];
}

/** 401s that are expected answers rather than "your session expired". */
function isExcludedFrom401Redirect(req: HttpRequest<unknown>): boolean {
  const path = pathOf(req.url);
  return (path === AUTH_ME_URL && req.method === 'GET') || path === AUTH_LOGIN_URL;
}

function errorCode(err: HttpErrorResponse): string | null {
  const body = err.error;
  return body && typeof body === 'object' && typeof body.code === 'string' ? body.code : null;
}

/**
 * - 401 from the API (except GET /auth/me and POST /auth/login): the session is gone — clear the
 *   user and send them to /login?returnUrl=<where they were>.
 * - 400 `{ code: 'antiforgery' }`: the XSRF-TOKEN cookie was missing/stale. Re-fetch /auth/me
 *   (the server re-issues the cookie) and retry the request once with the fresh token. The retry
 *   sets the header itself because Angular's XSRF interceptor runs before this one in the chain.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const xsrf = inject(HttpXsrfTokenExtractor);

  const send = (request: HttpRequest<unknown>): Observable<HttpEvent<unknown>> =>
    next(request).pipe(catchError((err: unknown) => handleError(request, err)));

  const handleError = (req: HttpRequest<unknown>, err: unknown): Observable<HttpEvent<unknown>> => {
    if (!(err instanceof HttpErrorResponse)) return throwError(() => err);

    if (err.status === 401 && !isExcludedFrom401Redirect(req)) {
      auth.clearSession();
      const current = router.url;
      if (!isPublicAuthUrl(current)) {
        const returnUrl = current && current !== '/' ? current : null;
        void router.navigate(['/login'], { queryParams: returnUrl ? { returnUrl } : {} });
      }
      return throwError(() => err);
    }

    if (
      err.status === 400 &&
      errorCode(err) === 'antiforgery' &&
      !req.context.get(ANTIFORGERY_RETRIED) &&
      pathOf(req.url) !== AUTH_ME_URL
    ) {
      return auth.refreshCurrentUser().pipe(
        switchMap(() => {
          const token = xsrf.getToken();
          const retried = req.clone({
            context: req.context.set(ANTIFORGERY_RETRIED, true),
            headers: token ? req.headers.set(XSRF_HEADER_NAME, token) : req.headers.delete(XSRF_HEADER_NAME)
          });
          return send(retried);
        })
      );
    }

    return throwError(() => err);
  };

  return send(req);
};
