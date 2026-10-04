import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { TodayApi, TodayDto } from './today.api';

describe('TodayApi', () => {
  let api: TodayApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(TodayApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('get() without a date GETs /api/v1/today with no params', () => {
    let result: TodayDto | undefined;
    api.get().subscribe((r) => (result = r));
    const req = http.expectOne('/api/v1/today');
    expect(req.request.method).toBe('GET');
    expect(req.request.params.has('date')).toBeFalse();
    const dto = { date: '2026-10-04' } as TodayDto;
    req.flush(dto);
    expect(result).toEqual(dto);
  });

  it('get(date) sends the date param', () => {
    api.get('2026-10-05').subscribe();
    expect(http.expectOne('/api/v1/today?date=2026-10-05').request.method).toBe('GET');
  });
});
