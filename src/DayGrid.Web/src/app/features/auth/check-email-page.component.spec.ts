import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed, discardPeriodicTasks, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { routeWithQuery } from '../../testing/auth-testing';
import { CheckEmailPageComponent, RESEND_COOLDOWN_SECONDS } from './check-email-page.component';

describe('CheckEmailPageComponent', () => {
  let fixture: ComponentFixture<CheckEmailPageComponent>;
  let el: HTMLElement;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [CheckEmailPageComponent],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(), routeWithQuery({ email: 'ada@example.com' })]
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  function resendButton(): HTMLButtonElement {
    return Array.from(el.querySelectorAll('button')).find((b) => /resend/i.test(b.textContent ?? ''))!;
  }

  it('shows the email and starts with a resend cooldown, then allows a resend', fakeAsync(() => {
    fixture = TestBed.createComponent(CheckEmailPageComponent);
    el = fixture.nativeElement;
    fixture.detectChanges();
    expect(el.textContent).toContain('ada@example.com');
    expect(resendButton().disabled).toBeTrue();
    expect(resendButton().textContent).toContain(`${RESEND_COOLDOWN_SECONDS}s`);

    tick(RESEND_COOLDOWN_SECONDS * 1000);
    fixture.detectChanges();
    expect(resendButton().disabled).toBeFalse();

    resendButton().click();
    const req = http.expectOne('/api/v1/auth/resend-confirmation');
    expect(req.request.body).toEqual({ email: 'ada@example.com' });
    req.flush({}, { status: 202, statusText: 'Accepted' });
    fixture.detectChanges();

    expect(el.textContent).toContain('A new verification email is on its way.');
    expect(resendButton().disabled).withContext('cooldown restarts').toBeTrue();
    fixture.destroy();
    discardPeriodicTasks();
  }));
});
