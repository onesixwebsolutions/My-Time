import { Page } from '@playwright/test';

import { createConfirmedUser, readState, signInViaUi, signOutViaUi, submitLogin } from './support/auth';
import { expect, test } from './support/fixtures';

// The /account page: profile (display name, time zone), change password, delete account.
// Each test uses its own fresh (non-admin) account, except the last-admin check.

async function openAccount(page: Page): Promise<void> {
  await page.locator('button[aria-haspopup="menu"]').click();
  await page.getByRole('menuitem', { name: 'Account' }).click();
  await expect(page).toHaveURL(/\/account$/);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Account');
}

test.describe('account page', () => {
  test.use({ storageState: { cookies: [], origins: [] } });

  test('change display name and time zone; persisted after reload', async ({ page, api }) => {
    const user = await createConfirmedUser('profile');
    await signInViaUi(page, user.email, user.password);
    await openAccount(page);
    await expect(page.locator('body')).toContainText(user.email);
    await expect(page.locator('#account-timezone')).toHaveValue('Asia/Kolkata');

    await page.locator('#account-name').fill('Renamed Person');
    await page.locator('#account-timezone').selectOption('Europe/London');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await expect(page.getByRole('status')).toHaveText('Profile saved.');
    await expect(page.locator('button[aria-haspopup="menu"]')).toHaveAttribute('aria-label', /Renamed Person/);

    await page.reload();
    await expect(page.locator('#account-name')).toHaveValue('Renamed Person');
    await expect(page.locator('#account-timezone')).toHaveValue('Europe/London');
    // The settings view follows the per-user time zone too.
    await page.goto('/settings');
    await expect(page.locator('dl')).toContainText('Europe/London');
    // ...and the admin's own settings are untouched.
    expect(((await (await api.get('/api/v1/settings')).json()) as { timeZone: string }).timeZone).toBe('Asia/Kolkata');
  });

  test('change password: wrong current password is rejected; new password works, old one fails', async ({ page, guard }) => {
    guard.allowApiError('/api/v1/auth/change-password', 400);
    guard.allowApiError('/api/v1/auth/login', 401);
    const user = await createConfirmedUser('chpw');
    await signInViaUi(page, user.email, user.password);
    await openAccount(page);

    const newPassword = 'my changed passphrase 7';
    await page.locator('#account-current-password').fill('not the current one');
    await page.locator('#account-new-password').fill(newPassword);
    await page.locator('#account-confirm-password').fill(newPassword);
    await page.getByRole('button', { name: 'Update password' }).click();
    await expect(page.locator('#account-current-password-error')).toHaveText('Current password is incorrect.');

    await page.locator('#account-current-password').fill(user.password);
    await page.getByRole('button', { name: 'Update password' }).click();
    await expect(page.getByRole('status')).toHaveText('Password updated.');
    // This session survives the change (the cookie was re-issued).
    await page.reload();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Account');

    await signOutViaUi(page);
    await submitLogin(page, user.email, user.password);
    await expect(page.getByTestId('login-error')).toContainText('Incorrect email or password.');
    await submitLogin(page, user.email, newPassword);
    await expect(page).toHaveURL(/\/today$/);
  });

  test('delete account (non-admin) after password confirmation; signing in afterwards fails', async ({ page, guard }) => {
    guard.allowApiError('/api/v1/auth/me', 400);
    guard.allowApiError('/api/v1/auth/login', 401);
    const user = await createConfirmedUser('delete');
    await signInViaUi(page, user.email, user.password);
    await openAccount(page);

    await page.getByRole('button', { name: 'Delete account…' }).click();
    const dialog = page.getByRole('alertdialog');
    await expect(dialog).toContainText('Delete your account?');
    await dialog.locator('#account-delete-password').fill('wrong password here');
    await dialog.getByRole('button', { name: 'Permanently delete' }).click();
    await expect(dialog.locator('#account-delete-password-error')).toHaveText('Incorrect password.');

    await dialog.locator('#account-delete-password').fill(user.password);
    await dialog.getByRole('button', { name: 'Permanently delete' }).click();
    await expect(page).toHaveURL(/\/login\?deleted=1$/);
    await expect(page.getByRole('status')).toContainText('Your account and all of its data have been deleted.');
    expect((await page.context().cookies()).some((c) => c.name === 'daygrid.auth')).toBe(false);

    await submitLogin(page, user.email, user.password);
    await expect(page.getByTestId('login-error')).toContainText('Incorrect email or password.');
  });
});

test('the only admin cannot delete their account (last_admin)', async ({ page, guard, api }) => {
  guard.allowApiError('/api/v1/auth/me', 400);
  const admin = readState().admin!;
  await page.goto('/today');
  await openAccount(page);
  await page.getByRole('button', { name: 'Delete account…' }).click();
  const dialog = page.getByRole('alertdialog');
  await dialog.locator('#account-delete-password').fill(admin.password);
  await dialog.getByRole('button', { name: 'Permanently delete' }).click();
  await expect(dialog.getByRole('alert')).toContainText('You are the only administrator');
  await dialog.getByRole('button', { name: 'Cancel' }).click();
  await expect(page).toHaveURL(/\/account$/);
  // Still signed in, still Admin.
  const me = (await (await api.get('/api/v1/auth/me')).json()) as { roles: string[] };
  expect(me.roles).toContain('Admin');
});
