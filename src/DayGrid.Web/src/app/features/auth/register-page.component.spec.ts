import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { routeWithQuery, submitForm, typeInto } from '../../testing/auth-testing';
import { RegisterPageComponent } from './register-page.component';

describe('RegisterPageComponent', () => {
  let fixture: ComponentFixture<RegisterPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let navigate: jasmine.Spy;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [RegisterPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), routeWithQuery({})]
    });
    http = TestBed.inject(HttpTestingController);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(RegisterPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  function fill(password = 'a long passphrase') {
    typeInto(el, '#register-name', ' Ada ');
    typeInto(el, '#register-email', 'ada@example.com ');
    typeInto(el, '#register-password', password);
  }

  it('uses new-password autocomplete and shows a strength hint', () => {
    expect(el.querySelector('#register-password')?.getAttribute('autocomplete')).toBe('new-password');
    typeInto(el, '#register-password', 'correct horse battery staple');
    fixture.detectChanges();
    expect(el.textContent).toContain('Strength: Strong');
  });

  it('show/hide toggle switches the password input type', () => {
    const input = el.querySelector('#register-password') as HTMLInputElement;
    const toggle = el.querySelector('button[aria-controls="register-password"]') as HTMLButtonElement;
    expect(input.type).toBe('password');
    toggle.click();
    fixture.detectChanges();
    expect(input.type).toBe('text');
    expect(toggle.getAttribute('aria-pressed')).toBe('true');
  });

  it('enforces the 10–128 character password policy client-side', () => {
    fill('too-short');
    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('Password must be at least 10 characters.');
    typeInto(el, '#register-password', 'x'.repeat(129));
    fixture.detectChanges();
    expect(el.textContent).toContain('Password must be at most 128 characters.');
    http.expectNone('/api/v1/auth/register');
  });

  it('on 202 navigates to the check-email page with the email', () => {
    fill();
    submitForm(el);
    const req = http.expectOne('/api/v1/auth/register');
    expect(req.request.body).toEqual({ email: 'ada@example.com', password: 'a long passphrase', displayName: 'Ada' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    expect(navigate).toHaveBeenCalledWith(['/register/check-email'], { queryParams: { email: 'ada@example.com' } });
  });

  it('maps server validation errors onto the fields', () => {
    fill();
    submitForm(el);
    http.expectOne('/api/v1/auth/register').flush(
      { errors: { password: ['Passwords must have at least 1 unique characters.'], email: ['Email is invalid.'] } },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();
    expect(el.querySelector('#register-password-error')?.textContent).toContain('unique characters');
    expect(el.querySelector('#register-email-error')?.textContent).toContain('Email is invalid.');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('shows a generic banner for unexpected errors', () => {
    fill();
    submitForm(el);
    http.expectOne('/api/v1/auth/register').flush({}, { status: 500, statusText: 'Error' });
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('Something went wrong');
  });
});
