import { AbstractControl, FormGroup, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';

import { PASSWORD_MAX_LENGTH, PASSWORD_MIN_LENGTH } from '../../core/auth/auth.models';

export const passwordValidators: ValidatorFn[] = [
  Validators.required,
  Validators.minLength(PASSWORD_MIN_LENGTH),
  Validators.maxLength(PASSWORD_MAX_LENGTH)
];

/** Group validator: `confirmKey` must equal `passwordKey` (error lands on the confirm control). */
export function matchValidator(passwordKey: string, confirmKey: string): ValidatorFn {
  return (group: AbstractControl): ValidationErrors | null => {
    const password = group.get(passwordKey);
    const confirm = group.get(confirmKey);
    if (!password || !confirm) return null;
    const mismatch = !!confirm.value && password.value !== confirm.value;
    const errors = { ...(confirm.errors ?? {}) };
    if (mismatch) errors['mismatch'] = true;
    else delete errors['mismatch'];
    const next = Object.keys(errors).length ? errors : null;
    if (JSON.stringify(next) !== JSON.stringify(confirm.errors)) confirm.setErrors(next);
    return null;
  };
}

/**
 * The message to show under a control: a server error (set by applyServerErrors) always shows;
 * client validation messages show once the field was touched or the form was submitted.
 */
export function controlError(control: AbstractControl | null, label: string, submitted: boolean): string | null {
  if (!control?.errors) return null;
  const e = control.errors;
  if (typeof e['server'] === 'string') return e['server'];
  if (!(control.touched || submitted)) return null;
  if (e['required']) return `${label} is required.`;
  if (e['email']) return 'Enter a valid email address.';
  if (e['minlength']) return `${label} must be at least ${e['minlength'].requiredLength} characters.`;
  if (e['maxlength']) return `${label} must be at most ${e['maxlength'].requiredLength} characters.`;
  if (e['mismatch']) return 'Passwords do not match.';
  return `${label} is invalid.`;
}

/**
 * Puts server `errors` { field: [msg] } onto the matching form controls. `aliases` maps a server
 * field name onto a control name when they differ. Returns messages for fields with no control,
 * so the caller can show them in the banner instead of dropping them.
 */
export function applyServerErrors(
  form: FormGroup,
  fieldErrors: Record<string, string[]>,
  aliases: Record<string, string> = {}
): string[] {
  const unmatched: string[] = [];
  for (const [field, messages] of Object.entries(fieldErrors)) {
    const control = form.get(aliases[field] ?? field);
    if (control) {
      control.setErrors({ ...(control.errors ?? {}), server: messages.join(' ') });
      control.markAsTouched();
    } else {
      unmatched.push(...messages);
    }
  }
  return unmatched;
}

export type PasswordStrength = { score: 0 | 1 | 2 | 3; label: string };

/** Deliberately simple hint (no composition rules per NIST 800-63B): mostly length, a bit of variety. */
export function passwordStrength(value: string | null | undefined): PasswordStrength {
  const v = value ?? '';
  if (v.length < PASSWORD_MIN_LENGTH) return { score: 0, label: `Too short — use at least ${PASSWORD_MIN_LENGTH} characters` };
  const unique = new Set(v).size;
  const classes = [/[a-z]/, /[A-Z]/, /\d/, /[^A-Za-z0-9]/].filter((r) => r.test(v)).length;
  let points = 0;
  if (v.length >= 14) points++;
  if (v.length >= 20) points++;
  if (classes >= 3) points++;
  if (unique < 5) points = 0;
  if (points >= 2) return { score: 3, label: 'Strong' };
  if (points === 1) return { score: 2, label: 'Good' };
  return { score: 1, label: 'Fair — a longer passphrase is stronger' };
}
