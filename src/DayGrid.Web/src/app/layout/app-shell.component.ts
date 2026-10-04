import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

import { NotificationLogEntry, NotificationsApi } from '../core/api/notifications.api';
import { AuthService } from '../core/auth/auth.service';

// Logo: the sidebar tile keeps its 32px / 9px-radius footprint and the accent->purple-500
// gradient; only the "D" glyph is replaced by the One Six constellation mark (inline so it
// inherits no external asset). Standalone files live in src/assets/brand/.
//
// Sidebar + topbar chrome translated from DayGrid-Mockup.html's <aside class="sidebar">
// and <header class="topbar"> markup. Layout uses Tailwind utilities; color values
// still come from the CSS custom properties in styles.scss via the tailwind.config.js
// var(...) mapping, so this component's classes (bg-raised, border-border, text-muted,
// etc.) automatically follow the active theme.
const POLL_INTERVAL_MS = 20_000;

/** localStorage key for the theme toggle (also read by the pre-paint script src/assets/theme-init.js). */
export const THEME_STORAGE_KEY = 'daygrid-theme';

type Theme = 'dark' | 'light';

function readStoredTheme(): Theme | null {
  try {
    const stored = localStorage.getItem(THEME_STORAGE_KEY);
    return stored === 'dark' || stored === 'light' ? stored : null;
  } catch {
    return null; // storage blocked (private mode / disabled site data)
  }
}

/** The theme to start with: the remembered choice, else whatever index.html set. */
function initialTheme(): Theme {
  const theme = readStoredTheme() ?? ((document.documentElement.dataset['theme'] as Theme | undefined) ?? 'dark');
  document.documentElement.dataset['theme'] = theme;
  return theme;
}

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  template: `
    <div class="grid min-h-screen grid-cols-1 md:grid-cols-[232px_1fr]">
      <aside
        class="flex min-w-0 flex-row items-center gap-1 overflow-x-auto border-b border-border bg-raised p-2 md:sticky md:top-0 md:h-screen md:flex-col md:items-stretch md:overflow-visible md:border-b-0 md:border-r md:p-3.5"
      >
        <div class="flex flex-none items-center gap-2.5 px-2 md:pb-5 md:pt-1">
          <div
            class="grid h-8 w-8 place-items-center rounded-[9px] bg-gradient-to-br from-accent to-purple-500 shadow-[0_4px_12px_rgba(99,102,241,.35)]"
          >
            <svg
              viewBox="0 0 128 128"
              class="h-[21px] w-[21px]"
              fill="#ffffff"
              role="img"
              aria-label="One Six"
            >
              <circle cx="64" cy="24" r="9.5" />
              <circle cx="98.64" cy="44" r="9.5" />
              <circle cx="98.64" cy="84" r="9.5" />
              <circle cx="64" cy="104" r="9.5" />
              <circle cx="29.36" cy="84" r="9.5" />
              <circle cx="29.36" cy="44" r="9.5" />
              <circle cx="64" cy="64" r="15" />
            </svg>
          </div>
          <div class="hidden md:block">
            <b class="block text-[16px] tracking-tight text-text">One Six</b>
            <span class="block text-[11px] font-medium text-muted">Checklist &amp; Timetable</span>
          </div>
        </div>

        <a
          routerLink="/today"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Today</a
        >
        <a
          routerLink="/checklists"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Checklists</a
        >
        <a
          routerLink="/timetable"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Timetable</a
        >
        <a
          routerLink="/upcoming"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Upcoming</a
        >
        <a
          routerLink="/tasks"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Tasks</a
        >
        <a
          routerLink="/expenses"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Expenses</a
        >

        <div class="hidden px-2.5 pb-1.5 pt-3.5 text-[10.5px] font-bold uppercase tracking-widest text-muted md:block">
          Insights
        </div>
        <a
          routerLink="/calendar"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Calendar</a
        >
        <a
          routerLink="/insights"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Streaks</a
        >

        <div class="hidden flex-1 md:block"></div>

        <a
          routerLink="/settings"
          routerLinkActive="bg-accent-soft text-accent font-semibold"
          class="flex flex-none items-center gap-2.5 whitespace-nowrap rounded-[9px] px-2.5 py-2 text-[13.5px] font-medium text-muted transition-colors hover:bg-raised2 hover:text-text"
          >Settings</a
        >
      </aside>

      <div class="flex min-w-0 flex-col">
        <header
          class="sticky top-0 z-20 flex h-[58px] items-center gap-3 border-b border-border bg-raised px-4 md:px-5"
        >
          <div
            class="hidden max-w-[340px] flex-1 items-center gap-2 rounded-[9px] border border-border bg-surface px-3 py-1.5 text-[13px] text-muted sm:flex"
          >
            <span>Search tasks, blocks, checklists…</span>
            <kbd class="ml-auto rounded border border-border px-1 text-[10px]">/</kbd>
          </div>
          <div class="flex-1"></div>

          <div class="relative">
            <button
              type="button"
              (click)="togglePanel()"
              class="relative grid h-[34px] w-[34px] place-items-center rounded-[9px] text-muted transition-colors hover:bg-raised2 hover:text-text"
              title="Notifications"
            >
              🔔
              @if (unreadCount() > 0) {
                <span
                  class="absolute right-0.5 top-0.5 grid h-[15px] min-w-[15px] place-items-center rounded-full border-2 border-raised bg-danger px-1 text-[9.5px] font-bold text-white"
                >
                  {{ unreadCount() }}
                </span>
              }
            </button>

            @if (panelOpen()) {
              <div class="fixed inset-0 z-10" (click)="panelOpen.set(false)"></div>
              <div class="absolute right-0 top-[42px] z-20 w-[340px] max-w-[calc(100vw-2rem)] overflow-hidden rounded-card border border-border bg-raised shadow-[var(--shadow)]">
                <div class="flex items-center gap-2 border-b border-border px-3.5 py-2.5">
                  <h3 class="text-[11.5px] font-bold uppercase tracking-wider text-muted">Notifications</h3>
                  @if (unreadCount() > 0) {
                    <button type="button" (click)="markAllRead()" class="ml-auto text-[11px] font-semibold text-accent hover:underline">
                      Mark all read
                    </button>
                  }
                </div>
                <div class="max-h-[400px] overflow-y-auto">
                  @if (loadingNotifications()) {
                    <div class="px-3.5 py-6 text-center text-[12.5px] text-muted">Loading…</div>
                  } @else if (notifications().length === 0) {
                    <div class="px-3.5 py-6 text-center text-[12.5px] text-muted">No notifications yet.</div>
                  } @else {
                    @for (n of notifications(); track n.id) {
                      <button
                        type="button"
                        (click)="markRead(n)"
                        class="flex w-full items-start gap-2 border-b border-border px-3.5 py-2.5 text-left transition-colors last:border-b-0 hover:bg-raised2"
                      >
                        <span
                          class="mt-1.5 h-1.5 w-1.5 flex-none rounded-full"
                          [class.bg-accent]="!n.readAtUtc"
                          [class.bg-transparent]="!!n.readAtUtc"
                        ></span>
                        <div class="min-w-0 flex-1">
                          <b class="block text-[12.5px] font-semibold" [class.text-muted]="!!n.readAtUtc">{{ n.title }}</b>
                          <span class="mt-0.5 block text-[11.5px] text-muted">{{ n.body }}</span>
                          <span class="mt-0.5 block text-[10.5px] text-muted">
                            {{ n.channel }}{{ n.error ? ' · failed' : '' }} · {{ relativeTime(n.createdAtUtc) }}
                          </span>
                        </div>
                      </button>
                    }
                  }
                </div>
              </div>
            }
          </div>

          <button
            type="button"
            (click)="toggleTheme()"
            class="grid h-[34px] w-[34px] place-items-center rounded-[9px] text-muted transition-colors hover:bg-raised2 hover:text-text"
            title="Toggle theme"
          >
            {{ theme() === 'dark' ? '☾' : '☀' }}
          </button>

          <div class="relative">
            <button
              type="button"
              (click)="userMenuOpen.set(!userMenuOpen())"
              aria-haspopup="menu"
              [attr.aria-expanded]="userMenuOpen()"
              aria-controls="user-menu"
              [attr.aria-label]="'Account menu for ' + (user()?.displayName || user()?.email)"
              class="flex h-[34px] items-center gap-2 rounded-[9px] px-1.5 text-muted transition-colors hover:bg-raised2 hover:text-text"
            >
              <span
                class="grid h-[26px] w-[26px] place-items-center rounded-full bg-accent-soft text-[11px] font-bold text-accent"
                aria-hidden="true"
                >{{ initials() }}</span
              >
              <span class="hidden max-w-[140px] truncate text-[12.5px] font-semibold text-text lg:block">{{
                user()?.displayName
              }}</span>
            </button>

            @if (userMenuOpen()) {
              <div class="fixed inset-0 z-10" (click)="userMenuOpen.set(false)"></div>
              <div
                id="user-menu"
                role="menu"
                (keydown.escape)="userMenuOpen.set(false)"
                class="absolute right-0 top-[42px] z-20 w-[240px] max-w-[calc(100vw-2rem)] overflow-hidden rounded-card border border-border bg-raised py-1 shadow-[var(--shadow)]"
              >
                <div class="border-b border-border px-3.5 py-2.5">
                  <b class="block truncate text-[13px] font-semibold text-text" data-testid="user-menu-name">{{ user()?.displayName }}</b>
                  <span class="block truncate text-[11.5px] text-muted" data-testid="user-menu-email">{{ user()?.email }}</span>
                </div>
                <a
                  role="menuitem"
                  routerLink="/account"
                  (click)="userMenuOpen.set(false)"
                  class="block px-3.5 py-2 text-[13px] text-text hover:bg-raised2"
                  >Account</a
                >
                @if (isAdmin()) {
                  <a
                    role="menuitem"
                    routerLink="/admin/users"
                    (click)="userMenuOpen.set(false)"
                    class="block px-3.5 py-2 text-[13px] text-text hover:bg-raised2"
                    >Admin · Users</a
                  >
                }
                <button
                  type="button"
                  role="menuitem"
                  (click)="signOut()"
                  [disabled]="signingOut()"
                  class="block w-full border-t border-border px-3.5 py-2 text-left text-[13px] text-danger hover:bg-raised2 disabled:opacity-60"
                >
                  {{ signingOut() ? 'Signing out…' : 'Sign out' }}
                </button>
              </div>
            }
          </div>
        </header>

        <div class="mx-auto w-full max-w-[1400px] flex-1 px-4 py-5 pb-16 md:px-6">
          <router-outlet></router-outlet>
        </div>
      </div>
    </div>
  `
})
export class AppShellComponent implements OnInit, OnDestroy {
  private readonly api = inject(NotificationsApi);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private pollHandle?: ReturnType<typeof setInterval>;

  protected readonly user = this.auth.currentUser;
  protected readonly isAdmin = this.auth.isAdmin;
  protected readonly userMenuOpen = signal(false);
  protected readonly signingOut = signal(false);
  protected readonly initials = computed(() => {
    const u = this.user();
    const source = (u?.displayName || u?.email || '?').trim();
    const parts = source.split(/\s+/).filter(Boolean);
    const letters = parts.length > 1 ? parts[0][0] + parts[parts.length - 1][0] : source.slice(0, 2);
    return letters.toUpperCase();
  });

  protected readonly theme = signal<Theme>(initialTheme());

  protected readonly notifications = signal<NotificationLogEntry[]>([]);
  protected readonly unreadCount = signal(0);
  protected readonly loadingNotifications = signal(false);
  protected readonly panelOpen = signal(false);

  ngOnInit(): void {
    this.refreshUnreadCount();
    this.pollHandle = setInterval(() => this.refreshUnreadCount(), POLL_INTERVAL_MS);
  }

  ngOnDestroy(): void {
    if (this.pollHandle) clearInterval(this.pollHandle);
  }

  /** POST /auth/logout (state is cleared even if it fails), then back to the login page. */
  protected signOut(): void {
    if (this.signingOut()) return;
    this.signingOut.set(true);
    const done = () => {
      this.signingOut.set(false);
      this.userMenuOpen.set(false);
      void this.router.navigateByUrl('/login');
    };
    this.auth.logout().subscribe({ complete: done, error: done });
  }

  protected toggleTheme(): void {
    const next = this.theme() === 'dark' ? 'light' : 'dark';
    this.theme.set(next);
    document.documentElement.dataset['theme'] = next;
    try {
      localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      /* storage blocked — the toggle still works for this page load */
    }
  }

  protected togglePanel(): void {
    const opening = !this.panelOpen();
    this.panelOpen.set(opening);
    if (opening) this.loadNotifications();
  }

  private refreshUnreadCount(): void {
    this.api.list(true, 100).subscribe({
      next: (unread) => this.unreadCount.set(unread.length),
      error: () => void 0
    });
  }

  private loadNotifications(): void {
    this.loadingNotifications.set(true);
    this.api.list(false, 20).subscribe({
      next: (list) => {
        this.notifications.set(list);
        this.loadingNotifications.set(false);
      },
      error: () => this.loadingNotifications.set(false)
    });
  }

  protected markRead(n: NotificationLogEntry): void {
    if (n.readAtUtc) return;
    this.notifications.update((list) => list.map((x) => (x.id === n.id ? { ...x, readAtUtc: new Date().toISOString() } : x)));
    this.unreadCount.update((c) => Math.max(0, c - 1));
    this.api.markRead(n.id).subscribe({ error: () => this.refreshUnreadCount() });
  }

  protected markAllRead(): void {
    const now = new Date().toISOString();
    this.notifications.update((list) => list.map((x) => ({ ...x, readAtUtc: x.readAtUtc ?? now })));
    this.unreadCount.set(0);
    this.api.markAllRead().subscribe({ error: () => this.refreshUnreadCount() });
  }

  protected relativeTime(iso: string): string {
    const diffMs = Date.now() - new Date(iso).getTime();
    const minutes = Math.round(diffMs / 60000);
    if (minutes < 1) return 'just now';
    if (minutes < 60) return `${minutes}m ago`;
    const hours = Math.round(minutes / 60);
    if (hours < 24) return `${hours}h ago`;
    return `${Math.round(hours / 24)}d ago`;
  }
}
