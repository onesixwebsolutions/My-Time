import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { AdminUserPage } from '../auth/auth.models';

// Admin account management (auth contract, "Admin endpoints"). Admins manage accounts only —
// none of these endpoints expose another user's data.
const BASE = '/api/v1/admin/users';

@Injectable({ providedIn: 'root' })
export class AdminApi {
  private readonly http = inject(HttpClient);

  listUsers(search: string, page: number, pageSize: number): Observable<AdminUserPage> {
    const params = new HttpParams().set('search', search).set('page', page).set('pageSize', pageSize);
    return this.http.get<AdminUserPage>(BASE, { params });
  }

  lock(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/${encodeURIComponent(id)}/lock`, null);
  }

  unlock(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/${encodeURIComponent(id)}/unlock`, null);
  }

  resendConfirmation(id: string): Observable<void> {
    return this.http.post<void>(`${BASE}/${encodeURIComponent(id)}/resend-confirmation`, null);
  }

  deleteUser(id: string): Observable<void> {
    return this.http.delete<void>(`${BASE}/${encodeURIComponent(id)}`);
  }
}
