import { Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AuthService } from '../../core/auth/auth.service';
import { applyServerErrors, controlError, matchValidator, passwordValidators } from '../../shared/forms/form-errors';
import { PasswordInputComponent } from '../../shared/forms/password-input.component';
import { UI } from '../../shared/forms/ui-classes';

/** Landing page for the reset link: /reset-password?email=&token= */
@Component({
  selector: 'app-reset-password-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, PasswordInputComponent],
  template: `
    <h1 class="text-[20px] font-bold tracking-tight text-text">Choose a new password</h1>
    @if (email) {
      <p class="mb-5 mt-1 text-[13px] text-muted">For <b class="text-text">{{ email }}</b></p>
    }

    @if (!linkValid) {
      <div [class]="ui.errorBanner + ' my-4'" role="alert">
        This reset link is incomplete. <a routerLink="/forgot-password" [class]="ui.link">Request a new one</a>.
      </div>
    } @else {
      @if (error()) {
        <div [class]="ui.errorBanner + ' mb-4'" role="alert">
          {{ error() }}
          @if (tokenInvalid()) {
            <a routerLink="/forgot-password" [class]="ui.link">Request a new link</a>.
          }
        </div>
      }
      <form [formGroup]="form" (ngSubmit)="submit()" novalidate class="space-y-4">
        <div>
          <app-password-input
            [control]="form.controls.newPassword"
            inputId="reset-password"
            label="New password"
            autocomplete="new-password"
            [showStrength]="true"
            [error]="passwordError()"
          />
        </div>
        <div>
          <app-password-input
            [control]="form.controls.confirmPassword"
            inputId="reset-password-confirm"
            label="Confirm new password"
            autocomplete="new-password"
            [error]="confirmError()"
          />
        </div>
        <button type="submit" [disabled]="pending()" [class]="ui.primary + ' w-full'">
          {{ pending() ? 'Saving…' : 'Reset password' }}
        </button>
      </form>
    }

    <p class="mt-5 text-center text-[12.8px] text-muted"><a routerLink="/login" [class]="ui.link">Back to sign in</a></p>
  `
})
export class ResetPasswordPageComponent {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly params = inject(ActivatedRoute).snapshot.queryParamMap;

  protected readonly ui = UI;
  protected readonly email = this.params.get('email') ?? '';
  private readonly token = this.params.get('token') ?? '';
  protected readonly linkValid = !!this.email && !!this.token;

  protected readonly form = inject(NonNullableFormBuilder).group(
    {
      newPassword: ['', passwordValidators],
      confirmPassword: ['', Validators.required]
    },
    { validators: matchValidator('newPassword', 'confirmPassword') }
  );
  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly tokenInvalid = signal(false);

  protected passwordError(): string | null {
    return controlError(this.form.controls.newPassword, 'New password', this.submitted());
  }

  protected confirmError(): string | null {
    return controlError(this.form.controls.confirmPassword, 'Confirmation', this.submitted());
  }

  submit(): void {
    this.submitted.set(true);
    if (!this.linkValid || this.form.invalid || this.pending()) {
      this.form.markAllAsTouched();
      return;
    }
    this.pending.set(true);
    this.error.set(null);
    this.tokenInvalid.set(false);
    this.auth.resetPassword({ email: this.email, token: this.token, newPassword: this.form.controls.newPassword.value }).subscribe({
      next: () => {
        this.pending.set(false);
        void this.router.navigate(['/login'], { queryParams: { reset: 1 } });
      },
      error: (err: unknown) => {
        this.pending.set(false);
        const problem = toApiProblem(err);
        if (problem.code === 'invalid_token') {
          this.tokenInvalid.set(true);
          this.error.set('This reset link is invalid or has expired.');
        } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length) {
          const rest = applyServerErrors(this.form, problem.fieldErrors, { password: 'newPassword' });
          if (rest.length) this.error.set(rest.join(' '));
        } else {
          this.error.set(commonErrorMessage(problem));
        }
      }
    });
  }
}
