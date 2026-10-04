import { createConfirmedUser, newBrowserContext, readState, signInViaUi, signedInApi, submitLogin } from './support/auth';
import { expect, test } from './support/fixtures';

// /admin/users as the first account (Admin, the default storageState): list, search, lock and
// unlock another user, and the guards for the admin's own row and for non-admins.

test.describe('admin users page', () => {
  test('list, search, lock user B (cannot sign in) and unlock (can); own row cannot be locked', async ({ page, browser, guard }) => {
    const admin = readState().admin!;
    const b = await createConfirmedUser('bob');

    await page.goto('/today');
    await page.locator('button[aria-haspopup="menu"]').click();
    await page.getByRole('menuitem', { name: 'Admin · Users' }).click();
    await expect(page).toHaveURL(/\/admin\/users$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Users');

    const table = page.getByRole('table');
    const adminRow = table.locator('tbody tr', { hasText: admin.email });
    await expect(adminRow).toContainText('You');
    await expect(adminRow).toContainText('Admin');
    await expect(adminRow.getByRole('button', { name: `Lock ${admin.email}` })).toBeDisabled();
    await expect(adminRow.getByRole('button', { name: `Delete ${admin.email}` })).toBeDisabled();
    await expect(page.getByTestId('admin-range')).toHaveText(/^1–\d+ of \d+$/);

    await page.locator('#admin-search').fill(b.email.slice(0, 12).toUpperCase());
    await expect(table.locator('tbody tr')).toHaveCount(1);
    await expect(page.getByTestId('admin-range')).toHaveText('1–1 of 1');
    const bRow = table.locator('tbody tr', { hasText: b.email });
    await expect(bRow).toContainText('Confirmed');
    await page.locator('#admin-search').fill('no-such-user-anywhere');
    await expect(table).toContainText('No users found.');
    await page.locator('#admin-search').fill(b.email);
    await expect(bRow).toBeVisible();

    await bRow.getByRole('button', { name: `Lock ${b.email}` }).click();
    await expect(page.getByRole('status')).toHaveText(`Locked ${b.email}.`);
    await expect(bRow).toContainText('Locked');

    // B, in their own browser, cannot sign in while locked.
    const bContext = await newBrowserContext(browser);
    const bPage = await bContext.newPage();
    guard.watch(bPage);
    guard.allowApiError('/api/v1/auth/login', 401);
    try {
      await bPage.goto('/login');
      await submitLogin(bPage, b.email, b.password);
      await expect(bPage.getByTestId('login-error')).toContainText('locked');
      await expect(bPage).toHaveURL(/\/login$/);

      await bRow.getByRole('button', { name: `Unlock ${b.email}` }).click();
      await expect(page.getByRole('status')).toHaveText(`Unlocked ${b.email}.`);
      await expect(bRow).not.toContainText('Locked');

      await submitLogin(bPage, b.email, b.password);
      await expect(bPage).toHaveURL(/\/today$/);
    } finally {
      await bContext.close();
    }
  });

  test('a non-admin is redirected away from /admin/users, has no admin menu entry, and the API answers 403', async ({ browser, guard }) => {
    const b = await createConfirmedUser('nonadmin');
    const bContext = await newBrowserContext(browser);
    const bPage = await bContext.newPage();
    guard.watch(bPage);
    try {
      await signInViaUi(bPage, b.email, b.password);
      await bPage.goto('/admin/users');
      await expect(bPage).toHaveURL(/\/today$/);
      await bPage.locator('button[aria-haspopup="menu"]').click();
      await expect(bPage.getByRole('menuitem', { name: 'Account' })).toBeVisible();
      await expect(bPage.getByRole('menuitem', { name: 'Admin · Users' })).toHaveCount(0);
    } finally {
      await bContext.close();
    }

    const bApi = await signedInApi(b);
    try {
      const list = await bApi.get('/api/v1/admin/users');
      expect(list.status()).toBe(403);
      expect(await list.json()).toMatchObject({ status: 403, code: 'forbidden' });
      const lock = await bApi.post(`/api/v1/admin/users/${'00000000-0000-0000-0000-000000000001'}/lock`);
      expect(lock.status()).toBe(403);
    } finally {
      await bApi.dispose();
    }
  });
});
