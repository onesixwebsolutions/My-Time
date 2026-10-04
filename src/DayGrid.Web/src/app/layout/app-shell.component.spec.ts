import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { NotificationsApi } from '../core/api/notifications.api';
import { AppShellComponent, THEME_STORAGE_KEY } from './app-shell.component';

describe('AppShellComponent theme toggle', () => {
  let originalTheme: string | undefined;

  beforeEach(() => {
    originalTheme = document.documentElement.dataset['theme'];
    localStorage.removeItem(THEME_STORAGE_KEY);
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        { provide: NotificationsApi, useValue: { list: () => of([]), markRead: () => of(), markAllRead: () => of() } }
      ]
    });
  });

  afterEach(() => {
    localStorage.removeItem(THEME_STORAGE_KEY);
    if (originalTheme) document.documentElement.dataset['theme'] = originalTheme;
  });

  function toggleButton(fixture: { nativeElement: HTMLElement }): HTMLButtonElement {
    return fixture.nativeElement.querySelector('button[title="Toggle theme"]') as HTMLButtonElement;
  }

  it('remembers the chosen theme so a reload keeps it', () => {
    document.documentElement.dataset['theme'] = 'dark';
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();

    toggleButton(fixture).click();
    fixture.detectChanges();
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(localStorage.getItem(THEME_STORAGE_KEY)).toBe('light');
    fixture.destroy();

    // "Reload": index.html starts dark again, a fresh shell restores the stored choice.
    document.documentElement.dataset['theme'] = 'dark';
    const reloaded = TestBed.createComponent(AppShellComponent);
    reloaded.detectChanges();
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(toggleButton(reloaded).textContent?.trim()).toBe('☀');
  });

  it('falls back to the document theme when nothing is stored', () => {
    document.documentElement.dataset['theme'] = 'dark';
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    expect(toggleButton(fixture).textContent?.trim()).toBe('☾');
    expect(document.documentElement.dataset['theme']).toBe('dark');
  });
});
