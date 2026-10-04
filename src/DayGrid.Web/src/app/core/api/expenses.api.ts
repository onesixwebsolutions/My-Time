import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Full CRUD client for /api/v1/expenses/* — mirrors ExpensesEndpoints.cs. Both entities are
// flat (no navigation properties), so the wire shape is a direct 1:1 mirror of the C# entity —
// no DTO projection needed, no enum-string quirks (amount is a plain number, date/dates are
// plain ISO strings).

export interface ConstantExpense {
  id: string;
  name: string;
  amount: number;
  category: string | null;
  dayOfMonth: number | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface VaryingExpense {
  id: string;
  title: string;
  amount: number;
  category: string | null;
  date: string;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface ConstantExpenseRequest {
  name: string;
  amount: number;
  category?: string | null;
  dayOfMonth?: number | null;
  notes?: string | null;
}

export interface VaryingExpenseRequest {
  title: string;
  amount: number;
  category?: string | null;
  date: string;
  notes?: string | null;
}

// "My Spends" — a running log of money actually spent this month, entered as you go. Same wire
// shape as VaryingExpense, but a deliberately separate resource/list on the Expenses page.
export interface CompletedSpend {
  id: string;
  title: string;
  amount: number;
  category: string | null;
  date: string;
  notes: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CompletedSpendRequest {
  title: string;
  amount: number;
  category?: string | null;
  date: string;
  notes?: string | null;
}

const CONSTANT_BASE = '/api/v1/expenses/constant';
const VARYING_BASE = '/api/v1/expenses/varying';
const SPENDS_BASE = '/api/v1/expenses/spends';

@Injectable({ providedIn: 'root' })
export class ExpensesApi {
  private readonly http = inject(HttpClient);

  listConstant(includeInactive = false): Observable<ConstantExpense[]> {
    const params = new HttpParams().set('includeInactive', includeInactive);
    return this.http.get<ConstantExpense[]>(CONSTANT_BASE, { params });
  }

  createConstant(request: ConstantExpenseRequest): Observable<ConstantExpense> {
    return this.http.post<ConstantExpense>(CONSTANT_BASE, request);
  }

  updateConstant(id: string, request: ConstantExpenseRequest): Observable<ConstantExpense> {
    return this.http.put<ConstantExpense>(`${CONSTANT_BASE}/${id}`, request);
  }

  setConstantActive(id: string, active: boolean): Observable<ConstantExpense> {
    const params = new HttpParams().set('active', active);
    return this.http.patch<ConstantExpense>(`${CONSTANT_BASE}/${id}/active`, null, { params });
  }

  removeConstant(id: string): Observable<void> {
    return this.http.delete<void>(`${CONSTANT_BASE}/${id}`);
  }

  listVarying(from?: string, to?: string, category?: string): Observable<VaryingExpense[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    if (category) params = params.set('category', category);
    return this.http.get<VaryingExpense[]>(VARYING_BASE, { params });
  }

  createVarying(request: VaryingExpenseRequest): Observable<VaryingExpense> {
    return this.http.post<VaryingExpense>(VARYING_BASE, request);
  }

  updateVarying(id: string, request: VaryingExpenseRequest): Observable<VaryingExpense> {
    return this.http.put<VaryingExpense>(`${VARYING_BASE}/${id}`, request);
  }

  removeVarying(id: string): Observable<void> {
    return this.http.delete<void>(`${VARYING_BASE}/${id}`);
  }

  listSpends(from?: string, to?: string, category?: string): Observable<CompletedSpend[]> {
    let params = new HttpParams();
    if (from) params = params.set('from', from);
    if (to) params = params.set('to', to);
    if (category) params = params.set('category', category);
    return this.http.get<CompletedSpend[]>(SPENDS_BASE, { params });
  }

  createSpend(request: CompletedSpendRequest): Observable<CompletedSpend> {
    return this.http.post<CompletedSpend>(SPENDS_BASE, request);
  }

  updateSpend(id: string, request: CompletedSpendRequest): Observable<CompletedSpend> {
    return this.http.put<CompletedSpend>(`${SPENDS_BASE}/${id}`, request);
  }

  removeSpend(id: string): Observable<void> {
    return this.http.delete<void>(`${SPENDS_BASE}/${id}`);
  }
}
