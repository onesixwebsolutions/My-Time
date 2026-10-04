import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Observable, Subject, debounceTime, distinctUntilChanged } from 'rxjs';

import { AdminApi } from '../../core/api/admin.api';
import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AdminUserDto } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { UI } from '../../shared/forms/ui-classes';

export const ADMIN_PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

type Action = 'lock' | 'unlock' | 'resend' | 'delete';

/** Friendly text for the refusal codes the admin endpoints send (400 ProblemDetails `code`). */
export const ADMIN_ERROR_MESSAGES: Record<string, string> = {
  cannot_lock_self: 'You can’t lock your own account.',
  cannot_delete_self: 'You can’t delete your own account here — use the Account page.',
  last_admin: 'This is the only administrator account.'
};

/** Account management for admins: search, page, lock/unlock, resend confirmation, delete. */
@Component({
  selector: 'app-admin-users-page',
  imports: [FormsModule, DatePipe],
  template: `
    <div class="mb-5 flex flex-wrap items-end gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Users</h1>
        <p class="mt-1 text-[13px] text-muted">Manage accounts. Admins can't see other users' data.</p>
      </div>
      <div class="ml-auto w-full sm:w-[280px]">
        <label for="admin-search" class="sr-only">Search users</label>
        <input
          id="admin-search"
          type="search"
          placeholder="Search email or name…"
          [ngModel]="search()"
          (ngModelChange)="onSearch($event)"
          [class]="ui.input"
        />
      </div>
    </div>

    @if (message()) {
      <div [class]="ui.infoBanner + ' mb-4'" role="status">{{ message() }}</div>
    }
    @if (error()) {
      <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ error() }}</div>
    }

    <div class="overflow-x-auto rounded-card border border-border bg-raised">
      <table class="w-full min-w-[760px] text-left text-[12.8px]">
        <caption class="sr-only">User accounts</caption>
        <thead class="border-b border-border text-[11px] font-bold uppercase tracking-wider text-muted">
          <tr>
            <th scope="col" class="px-4 py-2.5">User</th>
            <th scope="col" class="px-4 py-2.5">Roles</th>
            <th scope="col" class="px-4 py-2.5">Status</th>
            <th scope="col" class="px-4 py-2.5">Created</th>
            <th scope="col" class="px-4 py-2.5">Last sign-in</th>
            <th scope="col" class="px-4 py-2.5 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-border">
          @if (loading() && users().length === 0) {
            <tr><td colspan="6" class="px-4 py-8 text-center text-muted">Loading users…</td></tr>
          } @else if (users().length === 0) {
            <tr><td colspan="6" class="px-4 py-8 text-center text-muted">No users found.</td></tr>
          }
          @for (u of users(); track u.id) {
            <tr [attr.data-user-id]="u.id">
              <td class="px-4 py-3">
                <b class="block font-semibold text-text">
                  {{ u.displayName }}
                  @if (isSelf(u)) {
                    <span class="ml-1 rounded bg-accent-soft px-1.5 py-0.5 text-[10.5px] font-semibold text-accent">You</span>
                  }
                </b>
                <span class="text-muted">{{ u.email }}</span>
              </td>
              <td class="px-4 py-3 text-muted">{{ u.roles.join(', ') }}</td>
              <td class="px-4 py-3">
                <span class="flex flex-wrap gap-1">
                  @if (u.emailConfirmed) {
                    <span class="rounded px-1.5 py-0.5 text-[11px] font-semibold text-success">Confirmed</span>
                  } @else {
                    <span class="rounded px-1.5 py-0.5 text-[11px] font-semibold text-warning">Unconfirmed</span>
                  }
                  @if (u.lockedOut) {
                    <span class="rounded px-1.5 py-0.5 text-[11px] font-semibold text-danger"
                      [attr.title]="u.disabled ? 'Locked by an administrator' : 'Temporarily locked after failed sign-ins'">Locked</span>
                  }
                </span>
              </td>
              <td class="px-4 py-3 text-muted">{{ u.createdAt | date: 'mediumDate' }}</td>
              <td class="px-4 py-3 text-muted">{{ u.lastLoginAt ? (u.lastLoginAt | date: 'medium') : 'Never' }}</td>
              <td class="px-4 py-3">
                <div class="flex justify-end gap-1.5">
                  @if (u.lockedOut) {
                    <button type="button" [class]="ui.secondary + ' !px-2.5 !py-1 text-[11.5px]'" [disabled]="isSelf(u) || busy(u.id)"
                      (click)="run(u, 'unlock')" [attr.aria-label]="'Unlock ' + u.email">Unlock</button>
                  } @else {
                    <button type="button" [class]="ui.secondary + ' !px-2.5 !py-1 text-[11.5px]'" [disabled]="isSelf(u) || busy(u.id)"
                      (click)="run(u, 'lock')" [attr.aria-label]="'Lock ' + u.email">Lock</button>
                  }
                  @if (!u.emailConfirmed) {
                    <button type="button" [class]="ui.secondary + ' !px-2.5 !py-1 text-[11.5px]'" [disabled]="isSelf(u) || busy(u.id)"
                      (click)="run(u, 'resend')" [attr.aria-label]="'Resend confirmation to ' + u.email">Resend</button>
                  }
                  <button type="button" [class]="ui.danger + ' !px-2.5 !py-1 text-[11.5px]'" [disabled]="isSelf(u) || busy(u.id)"
                    (click)="confirmDelete.set(u)" [attr.aria-label]="'Delete ' + u.email">Delete</button>
                </div>
              </td>
            </tr>
          }
        </tbody>
      </table>
    </div>

    <div class="mt-3 flex items-center gap-2 text-[12.5px] text-muted">
      <span data-testid="admin-range">{{ rangeLabel() }}</span>
      <div class="ml-auto flex gap-2">
        <button type="button" [class]="ui.secondary" [disabled]="page() <= 1 || loading()" (click)="goTo(page() - 1)">Previous</button>
        <button type="button" [class]="ui.secondary" [disabled]="page() >= pageCount() || loading()" (click)="goTo(page() + 1)">Next</button>
      </div>
    </div>

    @if (confirmDelete(); as target) {
      <div class="fixed inset-0 z-40 bg-black/50" (click)="confirmDelete.set(null)" aria-hidden="true"></div>
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="admin-delete-title"
        aria-describedby="admin-delete-desc"
        class="fixed left-1/2 top-1/2 z-50 w-[420px] max-w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 rounded-card border border-border bg-raised p-5 shadow-[var(--shadow)]"
        (keydown.escape)="confirmDelete.set(null)"
      >
        <h2 id="admin-delete-title" class="text-[16px] font-bold text-text">Delete {{ target.email }}?</h2>
        <p id="admin-delete-desc" class="mt-1 text-[13px] leading-relaxed text-muted">
          This permanently deletes the account and all of its data. This cannot be undone.
        </p>
        <div class="mt-5 flex justify-end gap-2">
          <button type="button" [class]="ui.secondary" (click)="confirmDelete.set(null)">Cancel</button>
          <button type="button" [class]="ui.danger" [disabled]="busy(target.id)" (click)="run(target, 'delete')">Delete account</button>
        </div>
      </div>
    }
  `
})
export class AdminUsersPageComponent implements OnInit {
  private readonly api = inject(AdminApi);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly searchInput = new Subject<string>();

  protected readonly ui = UI;
  protected readonly users = signal<AdminUserDto[]>([]);
  protected readonly total = signal(0);
  protected readonly page = signal(1);
  protected readonly search = signal('');
  protected readonly loading = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly message = signal<string | null>(null);
  protected readonly busyIds = signal<ReadonlySet<string>>(new Set());
  protected readonly confirmDelete = signal<AdminUserDto | null>(null);

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / ADMIN_PAGE_SIZE)));
  protected readonly rangeLabel = computed(() => {
    const total = this.total();
    if (total === 0) return '0 users';
    const start = (this.page() - 1) * ADMIN_PAGE_SIZE + 1;
    const end = Math.min(total, start + this.users().length - 1);
    return `${start}–${end} of ${total}`;
  });

  ngOnInit(): void {
    this.searchInput
      .pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged(), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        this.page.set(1);
        this.load();
      });
    this.load();
  }

  protected isSelf(u: AdminUserDto): boolean {
    return u.id === this.auth.currentUser()?.id;
  }

  protected busy(id: string): boolean {
    return this.busyIds().has(id);
  }

  onSearch(value: string): void {
    this.search.set(value);
    this.searchInput.next(value.trim());
  }

  goTo(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pageCount()));
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api.listUsers(this.search().trim(), this.page(), ADMIN_PAGE_SIZE).subscribe({
      next: (result) => {
        this.loading.set(false);
        this.users.set(result.items);
        this.total.set(result.total);
        // Deleting the last row of the last page: step back a page.
        if (result.items.length === 0 && result.total > 0 && this.page() > 1) this.goTo(this.pageCount());
      },
      error: (err: unknown) => {
        this.loading.set(false);
        this.error.set(commonErrorMessage(toApiProblem(err)));
      }
    });
  }

  run(user: AdminUserDto, action: Action): void {
    if (this.isSelf(user) || this.busy(user.id)) return;
    const calls: Record<Action, () => Observable<void>> = {
      lock: () => this.api.lock(user.id),
      unlock: () => this.api.unlock(user.id),
      resend: () => this.api.resendConfirmation(user.id),
      delete: () => this.api.deleteUser(user.id)
    };
    const done: Record<Action, string> = {
      lock: `Locked ${user.email}.`,
      unlock: `Unlocked ${user.email}.`,
      resend: `Confirmation email re-sent to ${user.email}.`,
      delete: `Deleted ${user.email}.`
    };
    this.setBusy(user.id, true);
    this.message.set(null);
    this.error.set(null);
    calls[action]().subscribe({
      next: () => {
        this.setBusy(user.id, false);
        if (action === 'delete') this.confirmDelete.set(null);
        this.message.set(done[action]);
        this.load();
      },
      error: (err: unknown) => {
        this.setBusy(user.id, false);
        if (action === 'delete') this.confirmDelete.set(null);
        const problem = toApiProblem(err);
        // Refresh first: load() clears the banner, and the message below must survive it.
        if (problem.status === 404) this.load();
        this.error.set(
          (problem.code && ADMIN_ERROR_MESSAGES[problem.code]) ||
            (problem.status === 404
              ? 'That user no longer exists.'
              : problem.status === 403
                ? 'You no longer have administrator access.'
                : problem.status === 400 && problem.title
                  ? problem.title
                  : commonErrorMessage(problem))
        );
      }
    });
  }

  private setBusy(id: string, value: boolean): void {
    this.busyIds.update((ids) => {
      const next = new Set(ids);
      if (value) next.add(id);
      else next.delete(id);
      return next;
    });
  }
}
