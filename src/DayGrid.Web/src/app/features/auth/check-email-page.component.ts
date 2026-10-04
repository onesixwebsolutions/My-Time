import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';

import { commonErrorMessage, toApiProblem } from '../../core/auth/api-problem';
import { AuthService } from '../../core/auth/auth.service';
import { UI } from '../../shared/forms/ui-classes';

export const RESEND_COOLDOWN_SECONDS = 60;

/** Shown after registration (202). Lets the user re-send the verification email, with a cooldown. */
@Component({
  selector: 'app-check-email-page',
  imports: [RouterLink],
  template: `
    <h1 class="text-[20px] font-bold tracking-tight text-text">Check your email</h1>
    <p class="mb-4 mt-2 text-[13px] leading-relaxed text-muted">
      We sent a verification link to
      @if (email) {
        <b class="text-text">{{ email }}</b>.
      } @else {
        your email address.
      }
      Open it to activate your account, then sign in.
    </p>

    @if (sent()) {
      <div [class]="ui.infoBanner + ' mb-4'" role="status">A new verification email is on its way.</div>
    }
    @if (error()) {
      <div [class]="ui.errorBanner + ' mb-4'" role="alert">{{ error() }}</div>
    }

    @if (email) {
      <button type="button" (click)="resend()" [disabled]="pending() || cooldown() > 0" [class]="ui.secondary + ' w-full'">
        @if (pending()) {
          Sending…
        } @else if (cooldown() > 0) {
          Resend email in {{ cooldown() }}s
        } @else {
          Resend verification email
        }
      </button>
    }

    <p class="mt-5 text-center text-[12.8px] text-muted">
      Didn't get it? Check your spam folder. <a routerLink="/login" [class]="ui.link">Back to sign in</a>
    </p>
  `
})
export class CheckEmailPageComponent implements OnInit {
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  private timer: ReturnType<typeof setInterval> | null = null;

  protected readonly ui = UI;
  protected readonly email = inject(ActivatedRoute).snapshot.queryParamMap.get('email') ?? '';
  protected readonly cooldown = signal(0);
  protected readonly pending = signal(false);
  protected readonly sent = signal(false);
  protected readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.destroyRef.onDestroy(() => this.stopTimer());
    // The registration email was just sent — don't offer an immediate resend.
    this.startCooldown();
  }

  resend(): void {
    if (!this.email || this.pending() || this.cooldown() > 0) return;
    this.pending.set(true);
    this.error.set(null);
    this.auth.resendConfirmation(this.email).subscribe({
      next: () => {
        this.pending.set(false);
        this.sent.set(true);
        this.startCooldown();
      },
      error: (err: unknown) => {
        this.pending.set(false);
        const problem = toApiProblem(err);
        this.error.set(commonErrorMessage(problem));
        if (problem.retryAfterSeconds) this.startCooldown(problem.retryAfterSeconds);
      }
    });
  }

  private startCooldown(seconds = RESEND_COOLDOWN_SECONDS): void {
    this.stopTimer();
    this.cooldown.set(seconds);
    this.timer = setInterval(() => {
      const next = this.cooldown() - 1;
      this.cooldown.set(Math.max(0, next));
      if (next <= 0) this.stopTimer();
    }, 1000);
  }

  private stopTimer(): void {
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
  }
}
