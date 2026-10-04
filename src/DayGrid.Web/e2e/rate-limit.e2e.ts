import { submitLogin } from './support/auth';
import { expect, test } from './support/fixtures';

// The production auth rate limit (10 requests / minute / client IP) is left in force for the
// whole suite. This test owns a fresh client IP (the `clientIp` fixture, sent as
// X-Forwarded-For), so it neither inherits nor spends another test's budget.
test.use({ storageState: { cookies: [], origins: [] } });

test('11 rapid wrong sign-ins → "too many attempts" with the Retry-After seconds', async ({ page, guard }) => {
  guard.allowApiError('/api/v1/auth/login', 401);
  guard.allowApiError('/api/v1/auth/login', 429);
  // An unknown address. It is "locked out" after 5 failures exactly like a real account (so
  // locked_out cannot reveal which addresses have accounts); the rate limit is separate.
  const email = `nobody-${Date.now()}@e2e.test`;

  await page.goto('/login');
  for (let attempt = 1; attempt <= 10; attempt++) {
    const response = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/login'));
    await submitLogin(page, email, `wrong password ${attempt}`);
    expect((await response).status(), `attempt ${attempt}`).toBe(401);
    await expect(page.getByTestId('login-error')).toContainText(
      attempt < 5 ? 'Incorrect email or password.' : 'Too many failed sign-in attempts.'
    );
  }

  const response = page.waitForResponse((r) => r.url().endsWith('/api/v1/auth/login'));
  await submitLogin(page, email, 'wrong password 11');
  const limited = await response;
  expect(limited.status()).toBe(429);
  const retryAfter = Number(limited.headers()['retry-after']);
  expect(retryAfter).toBeGreaterThan(0);
  expect(retryAfter).toBeLessThanOrEqual(60);
  expect(await limited.json()).toMatchObject({ status: 429, code: 'rate_limited' });
  await expect(page.getByTestId('login-error')).toHaveText(/Too many attempts, try again in \d+ s\./);
  const shown = Number(/in (\d+) s/.exec((await page.getByTestId('login-error').textContent()) ?? '')![1]);
  expect(shown).toBe(retryAfter);
});
