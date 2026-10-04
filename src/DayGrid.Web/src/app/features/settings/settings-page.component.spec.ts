import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';

import { AppSettings, SettingsApi } from '../../core/api/settings.api';
import { fakeAuthService, provideFakeAuth } from '../../testing/auth-testing';
import { SettingsPageComponent } from './settings-page.component';

describe('SettingsPageComponent', () => {
  let fixture: ComponentFixture<SettingsPageComponent>;
  let api: jasmine.SpyObj<SettingsApi>;

  beforeEach(() => {
    api = jasmine.createSpyObj<SettingsApi>('SettingsApi', ['get']);
    TestBed.configureTestingModule({
      imports: [SettingsPageComponent],
      providers: [{ provide: SettingsApi, useValue: api }, provideFakeAuth(fakeAuthService())]
    });
  });

  it('renders loaded settings', () => {
    api.get.and.returnValue(of({ timeZone: 'Europe/London', theme: 'dark' } as AppSettings));
    fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Europe/London');
  });

  it("shows the signed-in user's email (not a hard-coded owner) as the notification target", () => {
    api.get.and.returnValue(of({ timeZone: 'UTC', theme: 'dark', emailEnabled: true, emailTo: null } as AppSettings));
    fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[data-testid="settings-account-email"]')?.textContent).toContain('ada@example.com');
    expect(el.textContent).toContain('sent to ada@example.com');
  });

  it('ignores a legacy emailTo value: the server mails the account address', () => {
    api.get.and.returnValue(of({ timeZone: 'UTC', theme: 'dark', emailEnabled: true, emailTo: 'old-owner@example.com' } as AppSettings));
    fixture = TestBed.createComponent(SettingsPageComponent);
    fixture.detectChanges();
    const text: string = fixture.nativeElement.textContent;
    expect(text).toContain('sent to ada@example.com');
    expect(text).not.toContain('old-owner@example.com');
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
