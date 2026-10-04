import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, Subject, catchError, finalize, firstValueFrom, map, of, tap } from 'rxjs';

import {
  ChangePasswordRequest,
  LoginRequest,
  RegisterRequest,
  ResetPasswordRequest,
  UpdateProfileRequest,
  UserDto
} from './auth.models';

export const AUTH_BASE = '/api/v1/auth';
export const AUTH_ME_URL = `${AUTH_BASE}/me`;
export const AUTH_LOGIN_URL = `${AUTH_BASE}/login`;

/**
 * Client side of the cookie-based auth contract. The session itself lives in the HttpOnly
 * `daygrid.auth` cookie; this service only mirrors "who am I" (GET /auth/me) into a signal.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  private readonly user = signal<UserDto | null>(null);
  private readonly sessionEndedSubject = new Subject<void>();

  readonly currentUser = this.user.asReadonly();
  readonly isAuthenticated = computed(() => this.user() !== null);
  readonly isAdmin = computed(() => this.user()?.roles.includes('Admin') ?? false);
  /** Emits whenever a signed-in session ends (logout, account deletion, 401 from the API). */
  readonly sessionEnded$ = this.sessionEndedSubject.asObservable();

  /**
   * GET /auth/me. Resolves to the user, or null on 401 (or any other failure) — never errors.
   * Also makes the server issue the XSRF-TOKEN cookie, so it runs at app start.
   */
  refreshCurrentUser(): Observable<UserDto | null> {
    return this.http.get<UserDto>(AUTH_ME_URL).pipe(
      catchError(() => of(null)),
      tap((user) => this.setUser(user))
    );
  }

  /** APP_INITIALIZER entry point. */
  loadCurrentUser(): Promise<UserDto | null> {
    return firstValueFrom(this.refreshCurrentUser());
  }

  register(request: RegisterRequest): Observable<void> {
    return this.http.post<unknown>(`${AUTH_BASE}/register`, request).pipe(map(() => void 0));
  }

  confirmEmail(userId: string, token: string): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/confirm-email`, { userId, token });
  }

  resendConfirmation(email: string): Observable<void> {
    return this.http.post<unknown>(`${AUTH_BASE}/resend-confirmation`, { email }).pipe(map(() => void 0));
  }

  login(request: LoginRequest): Observable<UserDto> {
    return this.http.post<UserDto>(AUTH_LOGIN_URL, request).pipe(tap((user) => this.setUser(user)));
  }

  /** POST /auth/logout. Local state is cleared whether or not the call succeeds. */
  logout(): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/logout`, null).pipe(finalize(() => this.clearSession()));
  }

  forgotPassword(email: string): Observable<void> {
    return this.http.post<unknown>(`${AUTH_BASE}/forgot-password`, { email }).pipe(map(() => void 0));
  }

  resetPassword(request: ResetPasswordRequest): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/reset-password`, request);
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(`${AUTH_BASE}/change-password`, request);
  }

  updateProfile(request: UpdateProfileRequest): Observable<UserDto> {
    return this.http.put<UserDto>(AUTH_ME_URL, request).pipe(tap((user) => this.setUser(user)));
  }

  /** DELETE /auth/me with the password as confirmation; the server signs out on success. */
  deleteAccount(password: string): Observable<void> {
    return this.http.delete<void>(AUTH_ME_URL, { body: { password } }).pipe(tap(() => this.clearSession()));
  }

  /** Forget the signed-in user locally (the server cookie is gone or invalid). */
  clearSession(): void {
    const wasSignedIn = this.user() !== null;
    this.user.set(null);
    if (wasSignedIn) this.sessionEndedSubject.next();
  }

  private setUser(user: UserDto | null): void {
    if (user === null) {
      this.clearSession();
    } else {
      this.user.set(user);
    }
  }
}
