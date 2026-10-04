import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AppSettings, SettingsApi } from '../../core/api/settings.api';
import { SettingsPageComponent } from './settings-page.component';

describe('SettingsPageComponent', () => {
  let fixture: ComponentFixture<SettingsPageComponent>;
  let api: jasmine.SpyObj<SettingsApi>;

  beforeEach(() => {
    api = jasmine.createSpyObj<SettingsApi>('SettingsApi', ['get']);
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [{ provide: SettingsApi, useValue: api }]
    });
  });

  it('renders loaded settings', () => {
    api.get.and.returnValue(of({ timeZone: 'Europe/London', theme: 'dark' } as AppSettings));
    fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Europe/London');
  });

  it('shows an error instead of loading forever when the request fails', () => {
    api.get.and.returnValue(throwError(() => new Error('500')));
    fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('Could not load settings.');
    expect(text).not.toContain('Loading settings');
  });
});
