import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { PASSWORD_MAX_LENGTH } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { sanitizeReturnUrl } from '../../core/auth/return-url';
import { applyServerErrors, controlError } from '../../shared/forms/form-errors';
import { PasswordInputComponent } from '../../shared/forms/password-input.component';
import { UI } from '../../shared/forms/ui-classes';

export const LOGIN_MESSAGES = {
  invalid_credentials: 'Incorrect email or password.',
  email_not_confirmed: 'Please confirm your email address before signing in. Check your inbox for the verification link.',
  locked_out: 'Too many failed sign-in attempts. Your account is locked for 15 minutes — try again later or reset your password.'
} as const;

@Component({
  selector: 'app-login-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PasswordInputComponent],
  template: `
    <h1 class="text-[20px] font-bold tracking-tight text-text">Sign in</h1>
    <p class="mb-5 mt-1 text-[13px] text-muted">Welcome back. Sign in to your account.</p>

    @if (notice()) {
      <div [class]="ui.infoBanner + ' mb-4'" role="status">{{ notice() }}</div>
    }
    @if (error()) {
      <div [class]="ui.errorBanner + ' mb-4'" role="alert" data-testid="login-error">
        {{ error() }}
        @if (errorCode() === 'email_not_confirmed') {
          <div class="mt-2">
            <button
              type="button"
              (click)="resendVerification()"
              [disabled]="resendPending() || resendSent()"
              class="font-semibold text-accent hover:underline disabled:opacity-60"
            >
              {{ resendSent() ? 'Verification email sent' : 'Resend verification email' }}
            </button>
          </div>
        }
        @if (errorCode() === 'locked_out') {
          <div class="mt-2"><a routerLink="/forgot-password" [class]="ui.link">Reset your password</a></div>
        }
      </div>
    }
    @if (resendSent()) {
      <div [class]="ui.infoBanner + ' mb-4'" role="status">
        If that account still needs verifying, a new link is on its way to {{ form.controls.email.value }}.
      </div>
    }

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="space-y-4">
      <div>
        <label for="login-email" [class]="ui.label">Email</label>
        <input
          id="login-email"
          type="email"
          formControlName="email"
          autocomplete="email"
          inputmode="email"
          autocapitalize="none"
          spellcheck="false"
          [attr.aria-invalid]="emailError() ? 'true' : null"
          [attr.aria-describedby]="emailError() ? 'login-email-error' : null"
          [class]="ui.input"
        />
        @if (emailError(); as message) {
          <p id="login-email-error" [class]="ui.fieldError">{{ message }}</p>
        }
      </div>

      <div>
        <app-password-input
          [control]="form.controls.password"
          inputId="login-password"
          label="Password"
          autocomplete="current-password"
          [error]="passwordError()"
        />
      </div>

      <div class="flex items-center justify-between">
        <label class="flex items-center gap-2 text-[12.5px] text-text">
          <input type="checkbox" formControlName="rememberMe" class="h-4 w-4 accent-[var(--accent)]" />
          Remember me
        </label>
        <a routerLink="/forgot-password" [class]="ui.link + ' text-[12.5px]'">Forgot password?</a>
      </div>

      <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
        {{ pending() ? 'Signing in…' : 'Sign in' }}
      </button>
    </form>

    <p class="mt-5 text-center text-[12.8px] text-muted">
      New here? <a routerLink="/register" [class]="ui.link">Create an account</a>
    </p>
  `
})
export class LoginPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);

  protected readonly ui = UI;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.maxLength(PASSWORD_MAX_LENGTH)]],
    rememberMe: [false]
  });

  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly errorCode = signal<string | null>(null);
  protected readonly resendPending = signal(false);
  protected readonly resendSent = signal(false);
  protected readonly notice = signal<string | null>(this.noticeFromQuery());

  protected emailError(): string | null {
    return controlError(this.form.controls.email, 'Email', this.submitted());
  }

  protected passwordError(): string | null {
    return controlError(this.form.controls.password, 'Password', this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.error.set(null);
    this.errorCode.set(null);
    this.resendSent.set(false);
    const { email, password, rememberMe } = this.form.getRawValue();
    this.auth.login({ email: email.trim(), password, rememberMe }).subscribe({
      next: () => {
        this.pending.set(false);
        const returnUrl = sanitizeReturnUrl(this.route.snapshot.queryParamMap.get('returnUrl'));
        void this.router.navigateByUrl(returnUrl);
      },
      error: (err: unknown) => {
        this.pending.set(false);
        const problem = toApiProblem(err);
        const code = problem.code as keyof typeof LOGIN_MESSAGES | null;
        if (problem.status === 401 && code && code in LOGIN_MESSAGES) {
          this.errorCode.set(code);
          this.error.set(LOGIN_MESSAGES[code]);
        } else if (problem.status === 401) {
          this.errorCode.set('invalid_credentials');
          this.error.set(LOGIN_MESSAGES.invalid_credentials);
        } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length) {
          const rest = applyServerErrors(this.form, problem.fieldErrors);
          if (rest.length) this.error.set(rest.join(' '));
        } else {
          this.error.set(commonErrorMessage(problem));
        }
      }
    });
  }

  resendVerification(): void {
    const email = this.form.controls.email.value.trim();
    if (!email) return;
    this.resendPending.set(true);
    this.auth.resendConfirmation(email).subscribe({
      next: () => {
        this.resendPending.set(false);
        this.resendSent.set(true);
      },
      error: (err: unknown) => {
        this.resendPending.set(false);
        this.error.set(commonErrorMessage(toApiProblem(err)));
      }
    });
  }

  private noticeFromQuery(): string | null {
    const params = this.route.snapshot.queryParamMap;
    if (params.get('reset') === '1') return 'Your password has been reset. Sign in with your new password.';
    if (params.get('confirmed') === '1') return 'Your email is confirmed. You can sign in now.';
    if (params.get('deleted') === '1') return 'Your account and all of its data have been deleted.';
    if (params.has('returnUrl')) return 'Please sign in to continue.';
    return null;
  }
}
