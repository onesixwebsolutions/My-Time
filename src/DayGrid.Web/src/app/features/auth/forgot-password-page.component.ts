import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AuthService } from '../../core/auth/auth.service';
import { controlError } from '../../shared/forms/form-errors';
import { UI } from '../../shared/forms/ui-classes';

/** Always shows the same confirmation, whether or not the email has an account (no enumeration). */
@Component({
  selector: 'app-forgot-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    <h1 class="text-[20px] font-bold tracking-tight text-text">Reset your password</h1>

    @if (done()) {
      <div [class]="ui.infoBanner + ' mb-4 mt-4'" role="status" data-testid="forgot-confirmation">
        If an account exists for <b>{{ sentTo() }}</b>, we've sent a link to reset your password. The link expires
        after a while, so use it soon.
      </div>
    } @else {
      <p class="mb-5 mt-1 text-[13px] text-muted">Enter your account email and we'll send you a reset link.</p>
      @if (error()) {
        <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ error() }}</div>
      }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="space-y-4">
        <div>
          <label for="forgot-email" [class]="ui.label">Email</label>
          <input
            id="forgot-email"
            type="email"
            formControlName="email"
            autocomplete="email"
            inputmode="email"
            autocapitalize="none"
            spellcheck="false"
            [attr.aria-invalid]="emailError() ? 'true' : null"
            [attr.aria-describedby]="emailError() ? 'forgot-email-error' : null"
            [class]="ui.input"
          />
          @if (emailError(); as message) {
            <p id="forgot-email-error" [class]="ui.fieldError">{{ message }}</p>
          }
        </div>
        <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
          {{ pending() ? 'Sending…' : 'Send reset link' }}
        </button>
      </form>
    }

    <p class="mt-5 text-center text-[12.8px] text-muted"><a routerLink="/login" [class]="ui.link">Back to sign in</a></p>
  `
})
export class ForgotPasswordPageComponent {
  private readonly auth = inject(AuthService);

  protected readonly ui = UI;
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]]
  });
  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly done = signal(false);
  protected readonly sentTo = signal('');
  protected readonly error = signal<string | null>(null);

  protected emailError(): string | null {
    return controlError(this.form.controls.email, 'Email', this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }
    const email = this.form.controls.email.value.trim();
    this.pending.set(true);
    this.error.set(null);
    this.auth.forgotPassword(email).subscribe({
      next: () => {
        this.pending.set(false);
        this.sentTo.set(email);
        this.done.set(true);
      },
      error: (err: unknown) => {
        this.pending.set(false);
        this.error.set(commonErrorMessage(toApiProblem(err)));
      }
    });
  }
}
