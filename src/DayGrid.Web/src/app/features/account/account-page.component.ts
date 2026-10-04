import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { DISPLAY_NAME_MAX_LENGTH } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { applyServerErrors, controlError, matchValidator, passwordValidators } from '../../shared/forms/form-errors';
import { PasswordInputComponent } from '../../shared/forms/password-input.component';
import { UI } from '../../shared/forms/ui-classes';

export const FALLBACK_TIME_ZONES = [
  'UTC',
  'Africa/Cairo',
  'Africa/Johannesburg',
  'Africa/Lagos',
  'America/Anchorage',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Mexico_City',
  'America/New_York',
  'America/Sao_Paulo',
  'America/Toronto',
  'Asia/Bangkok',
  'Asia/Dubai',
  'Asia/Hong_Kong',
  'Asia/Jakarta',
  'Asia/Kolkata',
  'Asia/Seoul',
  'Asia/Shanghai',
  'Asia/Singapore',
  'Asia/Tokyo',
  'Australia/Melbourne',
  'Australia/Sydney',
  'Europe/Berlin',
  'Europe/London',
  'Europe/Madrid',
  'Europe/Moscow',
  'Europe/Paris',
  'Pacific/Auckland'
];

/** IANA zones from the browser (Intl.supportedValuesOf), else a fallback list; always includes `current`. */
export function availableTimeZones(current: string | null | undefined): string[] {
  let zones: string[];
  try {
    const intl = Intl as unknown as { supportedValuesOf?: (key: string) => string[] };
    zones = intl.supportedValuesOf ? intl.supportedValuesOf('timeZone') : FALLBACK_TIME_ZONES;
    if (!zones.length) zones = FALLBACK_TIME_ZONES;
  } catch {
    zones = FALLBACK_TIME_ZONES;
  }
  const set = new Set(zones);
  set.add('UTC');
  if (current) set.add(current);
  return [...set].sort((a, b) => a.localeCompare(b));
}

export const LAST_ADMIN_MESSAGE =
  'You are the only administrator, so this account can’t be deleted. Make another account an administrator first.';

@Component({
  selector: 'app-account-page',
  standalone: true,
  imports: [ReactiveFormsModule, PasswordInputComponent],
  template: `
    <div class="mb-5 flex flex-wrap items-center gap-3">
      <div>
        <h1 class="text-[22px] font-bold tracking-tight text-text">Account</h1>
        <p class="mt-1 text-[13px] text-muted">
          Signed in as <b class="text-text">{{ user()?.email }}</b>
        </p>
      </div>
      <button type="button" (click)="logout()" [disabled]="loggingOut()" [class]="ui.secondary + ' ml-auto'">
        {{ loggingOut() ? 'Signing out…' : 'Sign out' }}
      </button>
    </div>

    <div class="grid max-w-[720px] gap-5">
      <!-- Profile -->
      <section [class]="ui.card" aria-labelledby="profile-heading">
        <h2 id="profile-heading" class="mb-4 text-[15px] font-bold text-text">Profile</h2>
        @if (profileMessage()) {
          <div [class]="ui.infoBanner + ' mb-4'" role="status">{{ profileMessage() }}</div>
        }
        @if (profileError()) {
          <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ profileError() }}</div>
        }
        <form [formGroup]="profileForm" (ngSubmit)="saveProfile()" novalidate class="grid gap-4 sm:grid-cols-2">
          <div>
            <label for="account-name" [class]="ui.label">Display name</label>
            <input
              id="account-name"
              type="text"
              formControlName="displayName"
              autocomplete="name"
              [attr.aria-invalid]="nameError() ? 'true' : null"
              [attr.aria-describedby]="nameError() ? 'account-name-error' : null"
              [class]="ui.input"
            />
            @if (nameError(); as message) {
              <p id="account-name-error" [class]="ui.fieldError">{{ message }}</p>
            }
          </div>
          <div>
            <label for="account-timezone" [class]="ui.label">Time zone</label>
            <select
              id="account-timezone"
              formControlName="timeZone"
              [attr.aria-invalid]="zoneError() ? 'true' : null"
              [attr.aria-describedby]="zoneError() ? 'account-timezone-error' : 'account-timezone-hint'"
              [class]="ui.input"
            >
              @for (zone of timeZones; track zone) {
                <option [value]="zone">{{ zone }}</option>
              }
            </select>
            @if (zoneError(); as message) {
              <p id="account-timezone-error" [class]="ui.fieldError">{{ message }}</p>
            } @else {
              <p id="account-timezone-hint" class="mt-1 text-[11.5px] text-muted">Used for reminders and your daily digest.</p>
            }
          </div>
          <div class="sm:col-span-2">
            <button type="submit" [disabled]="profilePending()" [class]="ui.primary">
              {{ profilePending() ? 'Saving…' : 'Save profile' }}
            </button>
          </div>
        </form>
      </section>

      <!-- Change password -->
      <section [class]="ui.card" aria-labelledby="password-heading">
        <h2 id="password-heading" class="mb-4 text-[15px] font-bold text-text">Change password</h2>
        @if (passwordMessage()) {
          <div [class]="ui.infoBanner + ' mb-4'" role="status">{{ passwordMessage() }}</div>
        }
        @if (passwordError()) {
          <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ passwordError() }}</div>
        }
        <form [formGroup]="passwordForm" (ngSubmit)="changePassword()" novalidate class="grid gap-4">
          <div>
            <app-password-input
              [control]="passwordForm.controls.currentPassword"
              inputId="account-current-password"
              label="Current password"
              autocomplete="current-password"
              [error]="pwError('currentPassword', 'Current password')"
            />
          </div>
          <div class="grid gap-4 sm:grid-cols-2">
            <div>
              <app-password-input
                [control]="passwordForm.controls.newPassword"
                inputId="account-new-password"
                label="New password"
                autocomplete="new-password"
                [showStrength]="true"
                [error]="pwError('newPassword', 'New password')"
              />
            </div>
            <div>
              <app-password-input
                [control]="passwordForm.controls.confirmPassword"
                inputId="account-confirm-password"
                label="Confirm new password"
                autocomplete="new-password"
                [error]="pwError('confirmPassword', 'Confirmation')"
              />
            </div>
          </div>
          <div>
            <button type="submit" [disabled]="passwordPending()" [class]="ui.primary">
              {{ passwordPending() ? 'Updating…' : 'Update password' }}
            </button>
          </div>
        </form>
      </section>

      <!-- Danger zone -->
      <section class="rounded-card border border-danger bg-raised p-5" aria-labelledby="delete-heading">
        <h2 id="delete-heading" class="text-[15px] font-bold text-danger">Delete account</h2>
        <p class="mb-4 mt-1 text-[13px] leading-relaxed text-muted">
          Permanently deletes your account and <b class="text-text">all</b> of your checklists, timetables, tasks,
          expenses and history. This cannot be undone.
        </p>
        <button type="button" (click)="openDelete()" [class]="ui.danger">Delete account…</button>
      </section>
    </div>

    @if (deleteOpen()) {
      <div class="fixed inset-0 z-40 bg-black/50" (click)="closeDelete()" aria-hidden="true"></div>
      <div
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="delete-dialog-title"
        aria-describedby="delete-dialog-desc"
        class="fixed left-1/2 top-1/2 z-50 w-[420px] max-w-[calc(100vw-2rem)] -translate-x-1/2 -translate-y-1/2 rounded-card border border-border bg-raised p-5 shadow-[var(--shadow)]"
        (keydown.escape)="closeDelete()"
      >
        <h2 id="delete-dialog-title" class="text-[16px] font-bold text-text">Delete your account?</h2>
        <p id="delete-dialog-desc" class="mb-4 mt-1 text-[13px] leading-relaxed text-muted">
          This permanently removes your account and all of its data. Enter your password to confirm.
        </p>
        @if (deleteError()) {
          <div [class]="ui.errorBanner + ' mb-3'" role="alert">{{ deleteError() }}</div>
        }
        <form [formGroup]="deleteForm" (ngSubmit)="deleteAccount()" novalidate>
          <app-password-input
            [control]="deleteForm.controls.password"
            inputId="account-delete-password"
            label="Password"
            autocomplete="current-password"
            [error]="deletePasswordError()"
          />
          <div class="mt-5 flex justify-end gap-2">
            <button type="button" (click)="closeDelete()" [class]="ui.secondary">Cancel</button>
            <button type="submit" [disabled]="deletePending()" [class]="ui.danger">
              {{ deletePending() ? 'Deleting…' : 'Permanently delete' }}
            </button>
          </div>
        </form>
      </div>
    }
  `
})
export class AccountPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly ui = UI;
  protected readonly user = this.auth.currentUser;
  protected readonly timeZones = availableTimeZones(this.user()?.timeZone);

  protected readonly profileForm = this.fb.group({
    displayName: [this.user()?.displayName ?? '', [Validators.required, Validators.maxLength(DISPLAY_NAME_MAX_LENGTH)]],
    timeZone: [this.user()?.timeZone ?? 'UTC', Validators.required]
  });
  protected readonly passwordForm = this.fb.group(
    {
      currentPassword: ['', Validators.required],
      newPassword: ['', passwordValidators],
      confirmPassword: ['', Validators.required]
    },
    { validators: matchValidator('newPassword', 'confirmPassword') }
  );
  protected readonly deleteForm = this.fb.group({ password: ['', Validators.required] });

  protected readonly profilePending = signal(false);
  protected readonly profileSubmitted = signal(false);
  protected readonly profileMessage = signal<string | null>(null);
  protected readonly profileError = signal<string | null>(null);

  protected readonly passwordPending = signal(false);
  protected readonly passwordSubmitted = signal(false);
  protected readonly passwordMessage = signal<string | null>(null);
  protected readonly passwordError = signal<string | null>(null);

  protected readonly deleteOpen = signal(false);
  protected readonly deletePending = signal(false);
  protected readonly deleteSubmitted = signal(false);
  protected readonly deleteError = signal<string | null>(null);

  protected readonly loggingOut = signal(false);

  protected nameError(): string | null {
    return controlError(this.profileForm.controls.displayName, 'Display name', this.profileSubmitted());
  }

  protected zoneError(): string | null {
    return controlError(this.profileForm.controls.timeZone, 'Time zone', this.profileSubmitted());
  }

  protected pwError(name: 'currentPassword' | 'newPassword' | 'confirmPassword', label: string): string | null {
    return controlError(this.passwordForm.controls[name], label, this.passwordSubmitted());
  }

  protected deletePasswordError(): string | null {
    return controlError(this.deleteForm.controls.password, 'Password', this.deleteSubmitted());
  }

  saveProfile(): void {
    this.profileSubmitted.set(true);
    if (this.profileForm.invalid || this.profilePending()) {
      this.profileForm.markAllAsTouched();
      return;
    }
    this.profilePending.set(true);
    this.profileMessage.set(null);
    this.profileError.set(null);
    const { displayName, timeZone } = this.profileForm.getRawValue();
    this.auth.updateProfile({ displayName: displayName.trim(), timeZone }).subscribe({
      next: (user) => {
        this.profilePending.set(false);
        this.profileForm.reset({ displayName: user.displayName, timeZone: user.timeZone });
        this.profileSubmitted.set(false);
        this.profileMessage.set('Profile saved.');
      },
      error: (err: unknown) => {
        this.profilePending.set(false);
        const problem = toApiProblem(err);
        if (problem.status === 400 && Object.keys(problem.fieldErrors).length) {
          const rest = applyServerErrors(this.profileForm, problem.fieldErrors);
          if (rest.length) this.profileError.set(rest.join(' '));
        } else {
          this.profileError.set(commonErrorMessage(problem));
        }
      }
    });
  }

  changePassword(): void {
    this.passwordSubmitted.set(true);
    if (this.passwordForm.invalid || this.passwordPending()) {
      this.passwordForm.markAllAsTouched();
      return;
    }
    this.passwordPending.set(true);
    this.passwordMessage.set(null);
    this.passwordError.set(null);
    const { currentPassword, newPassword } = this.passwordForm.getRawValue();
    this.auth.changePassword({ currentPassword, newPassword }).subscribe({
      next: () => {
        this.passwordPending.set(false);
        this.passwordForm.reset();
        this.passwordSubmitted.set(false);
        this.passwordMessage.set('Password updated.');
      },
      error: (err: unknown) => {
        this.passwordPending.set(false);
        const problem = toApiProblem(err);
        if (problem.code === 'invalid_credentials') {
          applyServerErrors(this.passwordForm, { currentPassword: ['Current password is incorrect.'] });
        } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length) {
          const rest = applyServerErrors(this.passwordForm, problem.fieldErrors, { password: 'newPassword' });
          if (rest.length) this.passwordError.set(rest.join(' '));
        } else {
          this.passwordError.set(commonErrorMessage(problem));
        }
      }
    });
  }

  openDelete(): void {
    this.deleteForm.reset();
    this.deleteSubmitted.set(false);
    this.deleteError.set(null);
    this.deleteOpen.set(true);
    setTimeout(() => document.getElementById('account-delete-password')?.focus());
  }

  closeDelete(): void {
    if (!this.deletePending()) this.deleteOpen.set(false);
  }

  deleteAccount(): void {
    this.deleteSubmitted.set(true);
    if (this.deleteForm.invalid || this.deletePending()) {
      this.deleteForm.markAllAsTouched();
      return;
    }
    this.deletePending.set(true);
    this.deleteError.set(null);
    this.auth.deleteAccount(this.deleteForm.controls.password.value).subscribe({
      next: () => {
        this.deletePending.set(false);
        this.deleteOpen.set(false);
        void this.router.navigate(['/login'], { queryParams: { deleted: 1 } });
      },
      error: (err: unknown) => {
        this.deletePending.set(false);
        const problem = toApiProblem(err);
        if (problem.code === 'invalid_credentials') {
          applyServerErrors(this.deleteForm, { password: ['Incorrect password.'] });
        } else if (problem.code === 'last_admin') {
          this.deleteError.set(LAST_ADMIN_MESSAGE);
        } else {
          this.deleteError.set(commonErrorMessage(problem));
        }
      }
    });
  }

  logout(): void {
    this.loggingOut.set(true);
    this.auth.logout().subscribe({
      error: () => this.afterLogout(),
      complete: () => this.afterLogout()
    });
  }

  private afterLogout(): void {
    this.loggingOut.set(false);
    void this.router.navigateByUrl('/login');
  }
}
