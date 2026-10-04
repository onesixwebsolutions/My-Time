import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AuthService } from '../../core/auth/auth.service';
import { controlError } from '../../shared/forms/form-errors';
import { UI } from '../../shared/forms/ui-classes';

type State = 'confirming' | 'success' | 'failed';

/** Landing page for the email verification link: /confirm-email?userId=&token= */
@Component({
  selector: 'app-confirm-email-page',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  template: `
    @switch (state()) {
      @case ('confirming') {
        <h1 class="text-[20px] font-bold tracking-tight text-text">Confirming your email…</h1>
        <p class="mt-2 text-[13px] text-muted" role="status">Hang on a moment.</p>
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
  private readonly route = inject(ActivatedRoute);

  protected readonly ui = UI;
  protected readonly state = signal<State>('confirming');
  protected readonly failure = signal('This verification link is invalid or has expired.');
  protected readonly pending = signal(false);
  protected readonly submitted = signal(false);
  protected readonly resent = signal(false);
  protected readonly resendError = signal<string | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    email: ['', [Validators.required, Validators.email]]
  });

  ngOnInit(): void {
    const params = this.route.snapshot.queryParamMap;
    const userId = params.get('userId');
    const token = params.get('token');
    if (!userId || !token) {
      this.failure.set('This verification link is incomplete.');
      this.state.set('failed');
      return;
    }
    this.auth.confirmEmail(userId, token).subscribe({
      next: () => this.state.set('success'),
      error: (err: unknown) => {
        const problem = toApiProblem(err);
        if (problem.code !== 'invalid_token' && problem.status !== 400) {
          this.failure.set(commonErrorMessage(problem));
        }
        this.state.set('failed');
      }
    });
  }

  protected emailError(): string | null {
    return controlError(this.form.controls.email, 'Email', this.submitted());
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
