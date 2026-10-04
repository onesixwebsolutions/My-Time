import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { AppSettings, SettingsApi } from './settings.api';

describe('SettingsApi', () => {
  let api: SettingsApi;
  let http: HttpTestingController;

  const settings: AppSettings = {
    timeZone: 'Europe/London',
    weekStartsOn: 1,
    dayStart: '06:00',
    dayEnd: '23:00',
    defaultSlotMinutes: 30,
    emailEnabled: false,
    emailTo: null,
    dailyDigestTime: null,
    theme: 'dark'
  };

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(SettingsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('get() GETs /api/v1/settings', () => {
    let result: AppSettings | undefined;
    api.get().subscribe((s) => (result = s));
    const req = http.expectOne('/api/v1/settings');
    expect(req.request.method).toBe('GET');
    req.flush(settings);
    expect(result).toEqual(settings);
  });

  it('update() PUTs the full settings object', () => {
    api.update(settings).subscribe();
    const req = http.expectOne('/api/v1/settings');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual(settings);
  });
});
