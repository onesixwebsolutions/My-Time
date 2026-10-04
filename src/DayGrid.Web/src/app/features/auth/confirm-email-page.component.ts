import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AuthService } from '../../core/auth/auth.service';
import { EmailLinkService } from '../../core/auth/email-link.service';
import { controlError } from '../../shared/forms/form-errors';
import { PasswordInputComponent } from '../../shared/forms/password-input.component';
import { UI } from '../../shared/forms/ui-classes';

type State = 'password' | 'success' | 'failed';

/**
 * Landing page for the email verification link: /confirm-email#userId=&token= (older links:
 * ?userId=&token=). The token is removed from the address bar as soon as it has been read. The
 * account password is asked for before confirming, so whoever registered an address they do not
 * own cannot get its owner to activate their account by clicking the link.
 */
@Component({
  selector: 'app-confirm-email-page',
  imports: [ReactiveFormsModule, RouterLink, PasswordInputComponent],
  template: `
    @switch (state()) {
      @case ('password') {
        <h1 class="text-[20px] font-bold tracking-tight text-text">Confirm your email</h1>
        <p class="mb-5 mt-2 text-[13px] text-muted">Enter the password you chose when you created your account.</p>

        @if (error()) {
          <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ error() }}</div>
        }

        <form [formGroup]="passwordForm" (ngSubmit)="confirm()" novalidate class="space-y-4">
          <div>
            <app-password-input
              [control]="passwordForm.controls.password"
              inputId="confirm-password"
              label="Password"
              autocomplete="current-password"
              [error]="passwordError()"
            />
          </div>
          <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
            {{ pending() ? 'Confirming…' : 'Confirm email' }}
          </button>
        </form>
        <p class="mt-5 text-center text-[12.8px] text-muted">
          Don't know this password? <a routerLink="/forgot-password" [class]="ui.link">Reset it</a> — that confirms your email too.
        </p>
      }
      @case ('success') {
        <h1 class="text-[20px] font-bold tracking-tight text-text">Email confirmed</h1>
        <p class="mb-5 mt-2 text-[13px] text-muted" role="status">Your account is active. You can sign in now.</p>
        <a routerLink="/login" [queryParams]="{ confirmed: 1 }" [class]="ui.primary + ' w-full'">Continue to sign in</a>
      }
      @case ('failed') {
        <h1 class="text-[20px] font-bold tracking-tight text-text">Link invalid or expired</h1>
        <p class="mb-4 mt-2 text-[13px] leading-relaxed text-muted" role="alert">
          {{ failure() }} Enter your email and we'll send you a new verification link.
        </p>

        @if (resent()) {
          <div [class]="ui.infoBanner + ' mb-4'" role="status">
            If that account still needs verifying, a new link is on its way.
          </div>
        }
        @if (resendError()) {
          <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ resendError() }}</div>
        }

        <form [formGroup]="form" (ngSubmit)="resend()" novalidate class="space-y-4">
          <div>
            <label for="confirm-email" [class]="ui.label">Email</label>
            <input
              id="confirm-email"
              type="email"
              formControlName="email"
              autocomplete="email"
              inputmode="email"
              autocapitalize="none"
              spellcheck="false"
              [attr.aria-invalid]="emailError() ? 'true' : null"
              [attr.aria-describedby]="emailError() ? 'confirm-email-error' : null"
              [class]="ui.input"
            />
            @if (emailError(); as message) {
              <p id="confirm-email-error" [class]="ui.fieldError">{{ message }}</p>
            }
          </div>
          <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
            {{ pending() ? 'Sending…' : 'Resend verification email' }}
          </button>
        </form>
        <p class="mt-5 text-center text-[12.8px] text-muted"><a routerLink="/login" [class]="ui.link">Back to sign in</a></p>
      }
    }
  `
})
export class ConfirmEmailPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly links = inject(EmailLinkService);
  private readonly fb = inject(NonNullableFormBuilder);

  protected readonly ui = UI;
  protected readonly state = signal<State>('password');
  protected readonly failure = signal('This verification link is invalid or has expired.');
  protected readonly error = signal<string | null>(null);
  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly passwordSubmitted = signal(false);
  protected readonly resent = signal(false);
  protected readonly resendError = signal<string | null>(null);
  protected readonly passwordForm = this.fb.group({ password: ['', Validators.required] });
  protected readonly form = this.fb.group({
    email: ['', [Validators.required, Validators.email]]
  });

  private userId = '';
  private token = '';

  ngOnInit(): void {
    const params = this.links.take(['userId', 'token']);
    this.userId = params['userId'] ?? '';
    this.token = params['token'] ?? '';
    if (!this.userId || !this.token) {
      this.failure.set('This verification link is incomplete.');
      this.state.set('failed');
    }
  }

  protected passwordError(): string | null {
    return controlError(this.passwordForm.controls.password, 'Password', this.passwordSubmitted());
  }

  protected emailError(): string | null {
    return controlError(this.form.controls.email, 'Email', this.submitted());
  }

  confirm(): void {
    this.passwordSubmitted.set(true);
    if (this.passwordForm.invalid || this.pending()) {
      this.passwordForm.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.error.set(null);
    this.auth.confirmEmail(this.userId, this.token, this.passwordForm.controls.password.value).subscribe({
      next: () => {
        this.pending.set(false);
        this.state.set('success');
      },
      error: (err: unknown) => {
        this.pending.set(false);
        const problem = toApiProblem(err);
        if (problem.code === 'invalid_credentials') {
          this.error.set('That password is not correct for this account.');
        } else if (problem.code === 'locked_out') {
          this.error.set('Too many wrong passwords. Try again in a few minutes, or reset your password.');
        } else if (problem.code === 'invalid_token' || problem.status === 400) {
          this.state.set('failed');
        } else {
          this.error.set(commonErrorMessage(problem));
        }
      }
    });
  }

  resend(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.resent.set(false);
    this.resendError.set(null);
    this.auth.resendConfirmation(this.form.controls.email.value.trim()).subscribe({
      next: () => {
        this.pending.set(false);
        this.resent.set(true);
      },
      error: (err: unknown) => {
        this.pending.set(false);
        this.resendError.set(commonErrorMessage(toApiProblem(err)));
      }
    });
  }
}
