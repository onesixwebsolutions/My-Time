import { FormControl, FormGroup } from '@angular/forms';

import { problem } from '../../testing/auth-testing';
import { applyServerErrors, controlError, matchValidator, passwordStrength } from '../../shared/forms/form-errors';
import { commonErrorMessage, normaliseFieldName, toApiProblem } from './api-problem';

describe('toApiProblem', () => {
  it('reads code, title, field errors (camel-cased) and Retry-After', () => {
    const p = toApiProblem(
      problem(400, { title: 'Bad', code: 'invalid_token', errors: { Password: ['Too short'], '$.email': ['Bad email'] } }, { 'Retry-After': '30' })
    );
    expect(p.status).toBe(400);
    expect(p.code).toBe('invalid_token');
    expect(p.title).toBe('Bad');
    expect(p.fieldErrors).toEqual({ password: ['Too short'], email: ['Bad email'] });
    expect(p.retryAfterSeconds).toBe(30);
  });

  it('copes with non-HTTP errors and empty bodies', () => {
    expect(toApiProblem(new Error('x')).status).toBe(0);
    expect(toApiProblem(problem(500)).code).toBeNull();
  });

  it('normaliseFieldName strips prefixes', () => {
    expect(normaliseFieldName('request.NewPassword')).toBe('newPassword');
  });

  it('commonErrorMessage covers 429, network and server errors', () => {
    expect(commonErrorMessage(toApiProblem(problem(429, {}, { 'Retry-After': '12' })))).toBe('Too many attempts, try again in 12 s.');
    expect(commonErrorMessage(toApiProblem(problem(429)))).toContain('try again in a minute');
    expect(commonErrorMessage(toApiProblem(problem(0)))).toContain('reach the server');
    expect(commonErrorMessage(toApiProblem(problem(500)))).toContain('Something went wrong');
  });
});

describe('form-errors helpers', () => {
  it('applyServerErrors sets control errors and returns unmatched messages', () => {
    const form = new FormGroup({ email: new FormControl(''), newPassword: new FormControl('') });
    const rest = applyServerErrors(form, { email: ['Taken'], password: ['Weak'], other: ['Nope'] }, { password: 'newPassword' });
    expect(controlError(form.controls.email, 'Email', false)).toBe('Taken');
    expect(controlError(form.controls.newPassword, 'Password', false)).toBe('Weak');
    expect(rest).toEqual(['Nope']);
  });

  it('matchValidator flags a mismatching confirmation and clears it when fixed', () => {
    const form = new FormGroup(
      { a: new FormControl('one-password'), b: new FormControl('two-password') },
      { validators: matchValidator('a', 'b') }
    );
    expect(form.controls.b.hasError('mismatch')).toBeTrue();
    form.controls.b.setValue('one-password');
    expect(form.controls.b.hasError('mismatch')).toBeFalse();
    expect(form.valid).toBeTrue();
  });

  it('passwordStrength grades mostly by length', () => {
    expect(passwordStrength('short').score).toBe(0);
    expect(passwordStrength('aaaaaaaaaaaa').score).toBe(1);
    expect(passwordStrength('correct horse battery staple').score).toBe(3);
  });
});
