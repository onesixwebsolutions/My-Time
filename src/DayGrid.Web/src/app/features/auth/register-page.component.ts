import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { DISPLAY_NAME_MAX_LENGTH, PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../core/auth/auth.models';
import { AuthService } from '../../core/auth/auth.service';
import { applyServerErrors, controlError, passwordValidators } from '../../shared/forms/form-errors';
import { PasswordInputComponent } from '../../shared/forms/password-input.component';
import { UI } from '../../shared/forms/ui-classes';

@Component({
  selector: 'app-register-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PasswordInputComponent],
  template: `
    <h1 class="text-[20px] font-bold tracking-tight text-text">Create your account</h1>
    <p class="mb-5 mt-1 text-[13px] text-muted">We'll email you a link to verify your address.</p>

    @if (error()) {
      <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ error() }}</div>
    }

    <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="space-y-4">
      <div>
        <label for="register-name" [class]="ui.label">Display name</label>
        <input
          id="register-name"
          type="text"
          formControlName="displayName"
          autocomplete="name"
          [attr.aria-invalid]="nameError() ? 'true' : null"
          [attr.aria-describedby]="nameError() ? 'register-name-error' : null"
          [class]="ui.input"
        />
        @if (nameError(); as message) {
          <p id="register-name-error" [class]="ui.fieldError">{{ message }}</p>
        }
      </div>

      <div>
        <label for="register-email" [class]="ui.label">Email</label>
        <input
          id="register-email"
          type="email"
          formControlName="email"
          autocomplete="email"
          inputmode="email"
          autocapitalize="none"
          spellcheck="false"
          [attr.aria-invalid]="emailError() ? 'true' : null"
          [attr.aria-describedby]="emailError() ? 'register-email-error' : null"
          [class]="ui.input"
        />
        @if (emailError(); as message) {
          <p id="register-email-error" [class]="ui.fieldError">{{ message }}</p>
        }
      </div>

      <div>
        <app-password-input
          [control]="form.controls.password"
          inputId="register-password"
          label="Password"
          autocomplete="new-password"
          [showStrength]="true"
          [hint]="passwordHint"
          [error]="passwordError()"
        />
      </div>

      <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
        {{ pending() ? 'Creating account…' : 'Create account' }}
      </button>
    </form>

    <p class="mt-5 text-center text-[12.8px] text-muted">
      Already have an account? <a routerLink="/login" [class]="ui.link">Sign in</a>
    </p>
  `
})
export class RegisterPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  protected readonly ui = UI;
  protected readonly passwordHint = `${PASSWORD_MIN_LENGTH}–${PASSWORD_MAX_LENGTH} characters. A long passphrase works well.`;
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayName: ['', [Validators.required, Validators.maxLength(DISPLAY_NAME_MAX_LENGTH)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', passwordValidators]
  });

  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<string | null>(null);

  protected nameError(): string | null {
    return controlError(this.form.controls.displayName, 'Display name', this.submitted());
  }

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
    const { displayName, email, password } = this.form.getRawValue();
    const trimmedEmail = email.trim();
    this.auth.register({ email: trimmedEmail, password, displayName: displayName.trim() }).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigate(['/register/check-email'], { queryParams: { email: trimmedEmail } });
      },
      error: (err: unknown) => {
        this.pending.set(false);
        const problem = toApiProblem(err);
        if (problem.status === 400 && Object.keys(problem.fieldErrors).length) {
          const rest = applyServerErrors(this.form, problem.fieldErrors);
          if (rest.length) this.error.set(rest.join(' '));
        } else {
          this.error.set(commonErrorMessage(problem));
        }
      }
    });
  }
}
