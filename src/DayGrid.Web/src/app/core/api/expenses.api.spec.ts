import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ExpensesApi } from './expenses.api';

describe('ExpensesApi', () => {
  let api: ExpensesApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ExpensesApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  describe('constant expenses', () => {
    const body = { name: 'Rent', amount: 1200, dayOfMonth: 1 };

    it('listConstant() sends includeInactive', () => {
      api.listConstant().subscribe();
      expect(http.expectOne('/api/v1/expenses/constant?includeInactive=false').request.method).toBe('GET');
      api.listConstant(true).subscribe();
      http.expectOne('/api/v1/expenses/constant?includeInactive=true');
    });

    it('createConstant() POSTs and updateConstant() PUTs the body', () => {
      api.createConstant(body).subscribe();
      let req = http.expectOne('/api/v1/expenses/constant');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);

      api.updateConstant('e1', body).subscribe();
      req = http.expectOne('/api/v1/expenses/constant/e1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);
    });

    it('setConstantActive() PATCHes with active param', () => {
      api.setConstantActive('e1', false).subscribe();
      const req = http.expectOne('/api/v1/expenses/constant/e1/active?active=false');
      expect(req.request.method).toBe('PATCH');
      expect(req.request.body).toBeNull();
    });

    it('removeConstant() DELETEs', () => {
      api.removeConstant('e1').subscribe();
      expect(http.expectOne('/api/v1/expenses/constant/e1').request.method).toBe('DELETE');
    });
  });

  describe('varying expenses', () => {
    const body = { title: 'Groceries', amount: 42.5, date: '2026-10-01' };

    it('listVarying() omits empty filters', () => {
      api.listVarying().subscribe();
      const req = http.expectOne('/api/v1/expenses/varying');
      expect(req.request.params.keys().length).toBe(0);
    });

    it('listVarying() sends from/to/category', () => {
      api.listVarying('2026-10-01', '2026-10-31', 'Food').subscribe();
      expect(http.expectOne('/api/v1/expenses/varying?from=2026-10-01&to=2026-10-31&category=Food').request.method).toBe('GET');
    });

    it('create/update/remove hit the right endpoints', () => {
      api.createVarying(body).subscribe();
      let req = http.expectOne('/api/v1/expenses/varying');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);

      api.updateVarying('v1', body).subscribe();
      req = http.expectOne('/api/v1/expenses/varying/v1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);

      api.removeVarying('v1').subscribe();
      expect(http.expectOne('/api/v1/expenses/varying/v1').request.method).toBe('DELETE');
    });
  });

  describe('completed spends', () => {
    const body = { title: 'Coffee', amount: 3.2, date: '2026-10-04' };

    it('listSpends() sends only provided filters', () => {
      api.listSpends('2026-10-01').subscribe();
      expect(http.expectOne('/api/v1/expenses/spends?from=2026-10-01').request.method).toBe('GET');
    });

    it('create/update/remove hit the right endpoints', () => {
      api.createSpend(body).subscribe();
      let req = http.expectOne('/api/v1/expenses/spends');
      expect(req.request.method).toBe('POST');
      expect(req.request.body).toEqual(body);

      api.updateSpend('s1', body).subscribe();
      req = http.expectOne('/api/v1/expenses/spends/s1');
      expect(req.request.method).toBe('PUT');
      expect(req.request.body).toEqual(body);

      api.removeSpend('s1').subscribe();
      expect(http.expectOne('/api/v1/expenses/spends/s1').request.method).toBe('DELETE');
    });
  });
});
