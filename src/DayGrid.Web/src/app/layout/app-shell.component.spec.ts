import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { NotificationsApi } from '../core/api/notifications.api';
import { FakeAuthService, fakeAuthService, makeUser, provideFakeAuth } from '../testing/auth-testing';
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
        provideFakeAuth(fakeAuthService()),
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

describe('AppShellComponent user menu', () => {
  let auth: FakeAuthService;

  function create(user = makeUser()) {
    auth = fakeAuthService(user);
    TestBed.configureTestingModule({
      imports: [AppShellComponent],
      providers: [
        provideRouter([]),
        provideFakeAuth(auth),
        { provide: NotificationsApi, useValue: { list: () => of([]), markRead: () => of(), markAllRead: () => of() } }
      ]
    });
    const fixture = TestBed.createComponent(AppShellComponent);
    fixture.detectChanges();
    return fixture;
  }

  function openMenu(fixture: ReturnType<typeof create>): HTMLElement {
    const el: HTMLElement = fixture.nativeElement;
    (el.querySelector('button[aria-haspopup="menu"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    return el.querySelector('#user-menu') as HTMLElement;
  }

  it('shows the signed-in user and an Account link, but no Admin link for normal users', () => {
    const fixture = create();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('button[aria-haspopup="menu"]')?.textContent).toContain('AL');
    const menu = openMenu(fixture);
    expect(menu.querySelector('[data-testid="user-menu-name"]')?.textContent).toContain('Ada Lovelace');
    expect(menu.querySelector('[data-testid="user-menu-email"]')?.textContent).toContain('ada@example.com');
    expect(menu.querySelector('a[href="/account"]')).toBeTruthy();
    expect(menu.querySelector('a[href="/admin/users"]')).toBeNull();
  });

  it('shows the Admin link for admins', () => {
    const menu = openMenu(create(makeUser({ roles: ['User', 'Admin'] })));
    expect(menu.querySelector('a[href="/admin/users"]')).toBeTruthy();
  });

  it('Sign out logs out, clears state and navigates to /login', () => {
    const fixture = create();
    const navigateByUrl = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    const menu = openMenu(fixture);
    (Array.from(menu.querySelectorAll('button')).find((b) => b.textContent?.includes('Sign out')) as HTMLButtonElement).click();
    expect(auth.logout).toHaveBeenCalled();
    expect(auth.currentUser()).toBeNull();
    expect(navigateByUrl).toHaveBeenCalledWith('/login');
  });
});
