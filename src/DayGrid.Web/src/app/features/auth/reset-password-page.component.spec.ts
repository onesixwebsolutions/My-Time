import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { emailLinkWith, provideEmailLink, submitForm, typeInto } from '../../testing/auth-testing';
import { ResetPasswordPageComponent } from './reset-password-page.component';

describe('ResetPasswordPageComponent', () => {
  let fixture: ComponentFixture<ResetPasswordPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let navigate: jasmine.Spy;

  let link: ReturnType<typeof emailLinkWith>;

  function create(query: Record<string, string> = { email: 'ada@example.com', token: 'tok-123' }) {
    link = emailLinkWith(query);
    TestBed.configureTestingModule({
      imports: [ResetPasswordPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideEmailLink(link)]
    });
    http = TestBed.inject(HttpTestingController);
    navigate = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(ResetPasswordPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  function fill(password: string, confirm: string) {
    typeInto(el, '#reset-password', password);
    typeInto(el, '#reset-password-confirm', confirm);
    submitForm(el);
    fixture.detectChanges();
  }

  it('requires matching passwords', () => {
    create();
    fill('a long passphrase', 'a different one!!');
    expect(el.textContent).toContain('Passwords do not match.');
    http.expectNone('/api/v1/auth/reset-password');
  });

  it('posts email, token and new password, then goes to login', () => {
    create();
    expect(link.take).toHaveBeenCalledOnceWith(['email', 'token']);
    fill('a long passphrase', 'a long passphrase');
    const req = http.expectOne('/api/v1/auth/reset-password');
    expect(req.request.body).toEqual({ email: 'ada@example.com', token: 'tok-123', newPassword: 'a long passphrase' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { reset: 1 } });
  });

  it('explains an invalid or expired token', () => {
    create();
    fill('a long passphrase', 'a long passphrase');
    http.expectOne('/api/v1/auth/reset-password').flush({ code: 'invalid_token' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.textContent).toContain('invalid or has expired');
    expect(el.querySelector('[role="alert"] a[href="/forgot-password"]')).toBeTruthy();
  });

  it('maps a newPassword validation error onto the field', () => {
    create();
    fill('a long passphrase', 'a long passphrase');
    http.expectOne('/api/v1/auth/reset-password').flush(
      { errors: { newPassword: ['Too common.'] } },
      { status: 400, statusText: 'Bad Request' }
    );
    fixture.detectChanges();
    expect(el.querySelector('#reset-password-error')?.textContent).toContain('Too common.');
  });

  it('shows an error for a link with no token', () => {
    create({ email: 'ada@example.com' });
    expect(el.textContent).toContain('This reset link is incomplete.');
    expect(el.querySelector('form')).toBeNull();
  });
});
