import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

// Lean client — the single-row app_settings resource (plan section 4.2 / 5.6).

export interface AppSettings {
  timeZone: string;
  weekStartsOn: number;
  dayStart: string;
  dayEnd: string;
  defaultSlotMinutes: number;
  emailEnabled: boolean;
  emailTo: string | null;
  dailyDigestTime: string | null;
  theme: 'system' | 'light' | 'dark';
}

const BASE = '/api/v1/settings';

@Injectable({ providedIn: 'root' })
export class SettingsApi {
  private readonly http = inject(HttpClient);

  get(): Observable<AppSettings> {
    return this.http.get<AppSettings>(BASE);
  }

  update(settings: AppSettings): Observable<AppSettings> {
    return this.http.put<AppSettings>(BASE, settings);
  }
}
