import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { AuthService } from '../../core/auth/auth.service';
import { makeUser, routeWithQuery, submitForm, typeInto } from '../../testing/auth-testing';
import { LOGIN_MESSAGES, LoginPageComponent } from './login-page.component';

describe('LoginPageComponent', () => {
  let fixture: ComponentFixture<LoginPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let navigateByUrl: jasmine.Spy;

  function create(query: Record<string, string> = {}) {
    TestBed.configureTestingModule({
      imports: [LoginPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), routeWithQuery(query)]
    });
    http = TestBed.inject(HttpTestingController);
    navigateByUrl = spyOn(TestBed.inject(Router), 'navigateByUrl').and.resolveTo(true);
    fixture = TestBed.createComponent(LoginPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  function fillAndSubmit(email = 'ada@example.com', password = 'correct-horse-1') {
    typeInto(el, '#login-email', email);
    typeInto(el, '#login-password', password);
    submitForm(el);
    fixture.detectChanges();
  }

  function failLogin(status: number, body: Record<string, unknown>) {
    http.expectOne('/api/v1/auth/login').flush(body, { status, statusText: 'Error' });
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('has labelled inputs with the right autocomplete hints', () => {
    create();
    expect(el.querySelector('label[for="login-email"]')).toBeTruthy();
    expect(el.querySelector('#login-email')?.getAttribute('autocomplete')).toBe('email');
    expect(el.querySelector('#login-password')?.getAttribute('autocomplete')).toBe('current-password');
  });

  it('shows validation errors and does not call the API for an empty form', () => {
    create();
    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('Email is required.');
    expect(el.textContent).toContain('Password is required.');
    http.expectNone('/api/v1/auth/login');
  });

  it('signs in, sends rememberMe, and goes to the sanitized returnUrl', () => {
    create({ returnUrl: '/checklists/3' });
    (el.querySelector('input[type="checkbox"]') as HTMLInputElement).click();
    fillAndSubmit();

    const submit = el.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).withContext('disabled while pending').toBeTrue();

    const req = http.expectOne('/api/v1/auth/login');
    expect(req.request.body).toEqual({ email: 'ada@example.com', password: 'correct-horse-1', rememberMe: true });
    req.flush(makeUser());
    expect(TestBed.inject(AuthService).isAuthenticated()).toBeTrue();
    expect(navigateByUrl).toHaveBeenCalledWith('/checklists/3');
  });

  it('ignores an off-site returnUrl', () => {
    create({ returnUrl: '//evil.example/phish' });
    fillAndSubmit();
    http.expectOne('/api/v1/auth/login').flush(makeUser());
    expect(navigateByUrl).toHaveBeenCalledWith('/');
  });

  it('shows invalid_credentials', () => {
    create();
    fillAndSubmit();
    failLogin(401, { code: 'invalid_credentials' });
    expect(el.textContent).toContain(LOGIN_MESSAGES.invalid_credentials);
    expect((el.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeFalse();
  });

  it('shows locked_out with a reset link', () => {
    create();
    fillAndSubmit();
    failLogin(401, { code: 'locked_out' });
    expect(el.textContent).toContain('locked for 15 minutes');
    expect(el.querySelector('[data-testid="login-error"] a[href="/forgot-password"]')).toBeTruthy();
  });

  it('email_not_confirmed offers to resend the verification email', () => {
    create();
    fillAndSubmit();
    failLogin(401, { code: 'email_not_confirmed' });
    expect(el.textContent).toContain('confirm your email');

    const resend = Array.from(el.querySelectorAll('button')).find((b) => b.textContent?.includes('Resend verification'))!;
    resend.click();
    const req = http.expectOne('/api/v1/auth/resend-confirmation');
    expect(req.request.body).toEqual({ email: 'ada@example.com' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Verification email sent');
  });

  it('shows a rate-limit message on 429', () => {
    create();
    fillAndSubmit();
    http.expectOne('/api/v1/auth/login').flush({}, { status: 429, statusText: 'Too Many', headers: { 'Retry-After': '42' } });
    fixture.detectChanges();
    expect(el.textContent).toContain('Too many attempts, try again in 42 s.');
  });

  it('shows a notice after a password reset', () => {
    create({ reset: '1' });
    expect(el.textContent).toContain('Your password has been reset');
  });
});
