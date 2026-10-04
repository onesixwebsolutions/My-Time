import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routeWithQuery, submitForm, typeInto } from '../../testing/auth-testing';
import { ForgotPasswordPageComponent } from './forgot-password-page.component';

describe('ForgotPasswordPageComponent', () => {
  let fixture: ComponentFixture<ForgotPasswordPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [ForgotPasswordPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), routeWithQuery({})]
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ForgotPasswordPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  });

  afterEach(() => http.verify());

  it('validates the email before calling the API', () => {
    typeInto(el, '#forgot-email', 'not-an-email');
    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('Enter a valid email address.');
    http.expectNone('/api/v1/auth/forgot-password');
  });

  it('always shows the same confirmation after a 202', () => {
    typeInto(el, '#forgot-email', 'nobody@example.com');
    submitForm(el);
    fixture.detectChanges();
    expect((el.querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBeTrue();
    const req = http.expectOne('/api/v1/auth/forgot-password');
    expect(req.request.body).toEqual({ email: 'nobody@example.com' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(el.querySelector('[data-testid="forgot-confirmation"]')?.textContent).toContain(
      'If an account exists for nobody@example.com'
    );
  });
});
