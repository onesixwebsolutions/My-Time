import { Component, input, signal } from '@angular/core';
import { FormControl, ReactiveFormsModule } from '@angular/forms';

import { UI } from './ui-classes';
import { passwordStrength } from './form-errors';

/** Password field with an accessible show/hide toggle and an optional strength hint. */
@Component({
  selector: 'app-password-input',
  imports: [ReactiveFormsModule],
  template: `
    <label [for]="inputId()" [class]="ui.label">{{ label() }}</label>
    <div class="relative">
      <input
        [id]="inputId()"
        [type]="visible() ? 'text' : 'password'"
        [formControl]="control()"
        [attr.autocomplete]="autocomplete()"
        [attr.aria-invalid]="error() ? 'true' : null"
        [attr.aria-describedby]="describedBy()"
        [attr.maxlength]="maxLength()"
        autocapitalize="none"
        spellcheck="false"
        [class]="ui.input + ' pr-16'"
      />
      <button
        type="button"
        (click)="visible.set(!visible())"
        [attr.aria-controls]="inputId()"
        [attr.aria-pressed]="visible()"
        [attr.aria-label]="(visible() ? 'Hide ' : 'Show ') + label().toLowerCase()"
        class="absolute right-1.5 top-1/2 -translate-y-1/2 rounded-md px-2 py-1 text-[11.5px] font-semibold text-muted hover:bg-raised2 hover:text-text"
      >
        {{ visible() ? 'Hide' : 'Show' }}
      </button>
    </div>
    @if (error(); as message) {
      <p [id]="inputId() + '-error'" [class]="ui.fieldError">{{ message }}</p>
    }
    @if (showStrength() && control().value) {
      <p [id]="inputId() + '-strength'" class="mt-1 flex items-center gap-2 text-[11.5px] text-muted" aria-live="polite">
        <span class="flex gap-0.5" aria-hidden="true">
          @for (i of bars; track i) {
            <span
              class="h-1 w-6 rounded-full"
              [class.bg-border]="strength().score < i"
              [class.bg-danger]="strength().score >= i && strength().score === 1"
              [class.bg-warning]="strength().score >= i && strength().score === 2"
              [class.bg-success]="strength().score >= i && strength().score === 3"
            ></span>
          }
        </span>
        <span>Strength: {{ strength().label }}</span>
      </p>
    } @else if (hint()) {
      <p [id]="inputId() + '-hint'" class="mt-1 text-[11.5px] text-muted">{{ hint() }}</p>
    }
  `
})
export class PasswordInputComponent {
  readonly control = input.required<FormControl<string>>();
  readonly inputId = input.required<string>();
  readonly label = input('Password');
  readonly autocomplete = input<'current-password' | 'new-password'>('current-password');
  readonly showStrength = input(false);
  readonly error = input<string | null>(null);
  readonly hint = input<string | null>(null);
  readonly maxLength = input<number | null>(null);

  protected readonly ui = UI;
  protected readonly visible = signal(false);
  protected readonly bars = [1, 2, 3];

  protected strength() {
    return passwordStrength(this.control().value);
  }

  protected describedBy(): string | null {
    const ids: string[] = [];
    if (this.error()) ids.push(this.inputId() + '-error');
    if (this.showStrength() && this.control().value) ids.push(this.inputId() + '-strength');
    else if (this.hint()) ids.push(this.inputId() + '-hint');
    return ids.length ? ids.join(' ') : null;
  }
}
