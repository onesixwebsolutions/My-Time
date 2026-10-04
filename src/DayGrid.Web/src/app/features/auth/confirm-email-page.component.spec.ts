import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routeWithQuery, submitForm, typeInto } from '../../testing/auth-testing';
import { ConfirmEmailPageComponent } from './confirm-email-page.component';

describe('ConfirmEmailPageComponent', () => {
  let fixture: ComponentFixture<ConfirmEmailPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;

  function create(query: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [ConfirmEmailPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), routeWithQuery(query)]
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ConfirmEmailPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
  }

  afterEach(() => http.verify());

  it('confirms on load and links to sign in', () => {
    create({ userId: 'u-1', token: 'abc_-123' });
    expect(el.textContent).toContain('Confirming');
    const req = http.expectOne('/api/v1/auth/confirm-email');
    expect(req.request.body).toEqual({ userId: 'u-1', token: 'abc_-123' });
    req.flush(null, { status: 204, statusText: 'No Content' });
    fixture.detectChanges();
    expect(el.textContent).toContain('Email confirmed');
    expect(el.querySelector('a[href^="/login"]')).toBeTruthy();
  });

  it('on invalid_token shows the resend form, which posts the email', () => {
    create({ userId: 'u-1', token: 'stale' });
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
