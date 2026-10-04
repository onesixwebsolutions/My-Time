import {
  confirmViaUi,
  createConfirmedUser,
  newUserData,
  readState,
  registerUser,
  signInViaUi,
  signOutViaUi,
  submitLogin
} from './support/auth';
import { countEmails, listEmails, waitForLink } from './support/email';
import { expect, test } from './support/fixtures';

// Registration, email confirmation, sign-in/out, password reset and returnUrl handling through
// the real UI. These start signed out; accounts are created per test, links come from the .eml
// files the API writes to its pickup folder.
test.use({ storageState: { cookies: [], origins: [] } });

const LOGIN = '/api/v1/auth/login';

test.describe('authentication', () => {
  test('register → check-email page → confirm link from .eml (with password) → sign in → Today', async ({ page, guard }) => {
    const state = readState();
    const user = newUserData('reg');
    const before = countEmails(state.pickupDir);

    await page.goto('/register');
    await page.locator('#register-name').fill(user.displayName);
    await page.locator('#register-email').fill(user.email);
    await page.locator('#register-password').fill(user.password);
    await page.getByRole('button', { name: 'Create account' }).click();

    await expect(page).toHaveURL(/\/register\/check-email\?email=/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Check your email');
    await expect(page.locator('body')).toContainText(user.email);

    const link = await waitForLink(state.pickupDir, user.email, '/confirm-email', { since: before });
    // The token travels in the fragment, so it never reaches a server (or its logs).
    expect(link.startsWith(`${state.baseURL}/confirm-email#userId=`)).toBe(true);
    expect(new URL(link).search).toBe('');
    await page.goto(link);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Confirm your email');
    // ...and it is removed from the address bar as soon as the page has read it.
    await expect(page).toHaveURL(/\/confirm-email$/);

    // The account password is required (someone who pre-registered this address can't make its owner activate it).
    guard.allowApiError('/api/v1/auth/confirm-email', 400);
    await page.locator('#confirm-password').fill('not my password');
    await page.getByRole('button', { name: 'Confirm email' }).click();
    await expect(page.getByRole('alert')).toContainText('That password is not correct');
    await page.locator('#confirm-password').fill(user.password);
    await page.getByRole('button', { name: 'Confirm email' }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Email confirmed');
    await page.getByRole('link', { name: 'Continue to sign in' }).click();
    await expect(page).toHaveURL(/\/login\?confirmed=1$/);
    await expect(page.getByRole('status')).toContainText('Your email is confirmed');

    await submitLogin(page, user.email, user.password);
    await expect(page).toHaveURL(/\/today$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.locator('button[aria-haspopup="menu"]')).toHaveAttribute('aria-label', new RegExp(user.displayName));
    // The session cookie is HttpOnly + Secure + SameSite=Strict, as in production.
    const auth = (await page.context().cookies()).find((c) => c.name === 'daygrid.auth');
    expect(auth).toMatchObject({ httpOnly: true, secure: true, sameSite: 'Strict' });
  });

  test('wrong password shows a clear message', async ({ page, guard }) => {
    guard.allowApiError(LOGIN, 401);
    const user = await createConfirmedUser('wrongpw');
    await page.goto('/login');
    await submitLogin(page, user.email, 'definitely not it');
    await expect(page.getByTestId('login-error')).toContainText('Incorrect email or password.');
    await expect(page).toHaveURL(/\/login$/);
    // Unknown accounts get exactly the same answer (no enumeration).
    await submitLogin(page, `nobody-${Date.now()}@e2e.test`, 'definitely not it');
    await expect(page.getByTestId('login-error')).toContainText('Incorrect email or password.');
  });

  test('unconfirmed account: sign-in explains, resend delivers a fresh link that works', async ({ page, guard }) => {
    guard.allowApiError(LOGIN, 401);
    const state = readState();
    const user = newUserData('unconfirmed');
    await registerUser(state.baseURL, user);
    await waitForLink(state.pickupDir, user.email, '/confirm-email');
    const mailsBefore = listEmails(state.pickupDir).filter((m) => m.to.includes(user.email)).length;
    const countBefore = countEmails(state.pickupDir);

    await page.goto('/login');
    await submitLogin(page, user.email, user.password);
    await expect(page.getByTestId('login-error')).toContainText('Please confirm your email address before signing in.');
    await page.getByRole('button', { name: 'Resend verification email' }).click();
    await expect(page.getByRole('button', { name: 'Verification email sent' })).toBeDisabled();
    await expect(page.getByRole('status')).toContainText(`a new link is on its way to ${user.email}`);

    const link = await waitForLink(state.pickupDir, user.email, '/confirm-email', { since: countBefore });
    expect(listEmails(state.pickupDir).filter((m) => m.to.includes(user.email)).length).toBe(mailsBefore + 1);
    await confirmViaUi(page, link, user.password);
    await page.goto('/login');
    await submitLogin(page, user.email, user.password);
    await expect(page).toHaveURL(/\/today$/);
  });

  test('forgot password → reset via .eml link → old password fails, new one works', async ({ page, guard }) => {
    guard.allowApiError(LOGIN, 401);
    const state = readState();
    const user = await createConfirmedUser('reset');
    const before = countEmails(state.pickupDir);

    await page.goto('/login');
    await page.getByRole('link', { name: 'Forgot password?' }).click();
    await expect(page).toHaveURL(/\/forgot-password$/);
    await page.locator('#forgot-email').fill(user.email);
    await page.getByRole('button', { name: 'Send reset link' }).click();
    await expect(page.getByTestId('forgot-confirmation')).toContainText(user.email);

    const link = await waitForLink(state.pickupDir, user.email, '/reset-password', { since: before });
    expect(link.startsWith(`${state.baseURL}/reset-password#email=`)).toBe(true);
    await page.goto(link);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Choose a new password');
    await expect(page.locator('body')).toContainText(user.email);
    await expect(page).toHaveURL(/\/reset-password$/); // token stripped from the address bar
    const newPassword = 'a brand new passphrase 42';
    await page.locator('#reset-password').fill(newPassword);
    await page.locator('#reset-password-confirm').fill(newPassword);
    await page.getByRole('button', { name: 'Reset password' }).click();
    await expect(page).toHaveURL(/\/login\?reset=1$/);
    await expect(page.getByRole('status')).toContainText('Your password has been reset');

    await submitLogin(page, user.email, user.password);
    await expect(page.getByTestId('login-error')).toContainText('Incorrect email or password.');
    await submitLogin(page, user.email, newPassword);
    await expect(page).toHaveURL(/\/today$/);
  });

  test('sign-out clears the session; a protected deep link goes to /login?returnUrl= and back', async ({ page }) => {
    const user = await createConfirmedUser('logout');
    await signInViaUi(page, user.email, user.password);
    await signOutViaUi(page);
    expect((await page.context().cookies()).some((c) => c.name === 'daygrid.auth')).toBe(false);

    // A fresh page load doesn't resurrect the session: the server no longer knows this browser.
    await page.goto('/today');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Ftoday$/);
    const me = await page.evaluate(async () => (await fetch('/api/v1/auth/me')).status);
    expect(me).toBe(401);

    await page.goto('/checklists?view=all');
    await expect(page).toHaveURL(/\/login\?returnUrl=%2Fchecklists%3Fview%3Dall$/);
    await expect(page.getByRole('status')).toContainText('Please sign in to continue.');
    await submitLogin(page, user.email, user.password);
    await expect(page).toHaveURL(/\/checklists\?view=all$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Checklists');
  });

  for (const evil of ['//evil.com', 'https://evil.com/steal', '/\\evil.com', 'javascript:alert(1)']) {
    test(`open-redirect attempt returnUrl=${evil} stays on the site`, async ({ page, baseURL }) => {
      const user = await createConfirmedUser('redirect');
      await page.goto(`/login?returnUrl=${encodeURIComponent(evil)}`);
      await submitLogin(page, user.email, user.password);
      await expect(page).toHaveURL(/\/today$/);
      expect(new URL(page.url()).origin).toBe(new URL(baseURL!).origin);
    });
  }
});
