import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { emailLinkWith, provideEmailLink, submitForm, typeInto } from '../../testing/auth-testing';
import { ConfirmEmailPageComponent } from './confirm-email-page.component';

describe('ConfirmEmailPageComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;
  let link: ReturnType<typeof emailLinkWith>;

  function create(params: Record<string, string>) {
    link = emailLinkWith(params);
    TestBed.configureTestingModule({
      imports: [ConfirmEmailPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), provideEmailLink(link)]
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConfirmEmailPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  function confirmWith(password: string) {
    typeInto(el, '#confirm-password', password);
    submitForm(el);
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('reads (and strips) the link parameters once, then asks for the password before confirming', () => {
    create({ userId: 'u-1', token: 'abc_-123' });
    expect(link.take).toHaveBeenCalledOnceWith(['userId', 'token']);
    expect(el.textContent).toContain('Confirm your email');
    http.expectNone('/api/v1/auth/confirm-email');

    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('Password is required.');
    http.expectNone('/api/v1/auth/confirm-email');

    confirmWith('my passphrase');
    const req = http.expectOne('/api/v1/auth/confirm-email');
    expect(req.request.body).toEqual({ userId: 'u-1', token: 'abc_-123', password: 'my passphrase' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Email confirmed');
    expect(el.querySelector('a[href^="/login"]')).toBeTruthy();
  });

  it('a wrong password keeps the form (the link stays usable) and offers a reset', () => {
    create({ userId: 'u-1', token: 'tok' });
    confirmWith('wrong one');
    http.expectOne('/api/v1/auth/confirm-email').flush({ code: 'invalid_credentials' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('not correct');
    expect(el.querySelector('#confirm-password')).toBeTruthy();
    expect(el.querySelector('a[href="/forgot-password"]')).toBeTruthy();

    confirmWith('right one');
    http.expectOne('/api/v1/auth/confirm-email').flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Email confirmed');
  });

  it('on invalid_token shows the resend form, which posts the email', () => {
    create({ userId: 'u-1', token: 'stale' });
    confirmWith('my passphrase');
    http.expectOne('/api/v1/auth/confirm-email').flush({ code: 'invalid_token' }, { status: 400, statusText: 'Bad Request' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Link invalid or expired');

    submitForm(el);
    fixture.detectChanges();
    expect(el.textContent).toContain('Email is required.');

    typeInto(el, '#confirm-email', 'ada@example.com');
    submitForm(el);
    const req = http.expectOne('/api/v1/auth/resend-confirmation');
    expect(req.request.body).toEqual({ email: 'ada@example.com' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();
    expect(el.textContent).toContain('a new link is on its way');
  });

  it('treats a link without userId/token as invalid without calling the API', () => {
    create({});
    http.expectNone('/api/v1/auth/confirm-email');
    expect(el.textContent).toContain('incomplete');
  });
});
