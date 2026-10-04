import { expect, test } from './support/fixtures';
import { ROUTES } from './support/routes';

// Runs first (its own project), against the freshly initialised database: only the
// app_settings row from db/init.sql exists.

test.describe('empty database', () => {
  test('Today renders its empty states and the shell works', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/today$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
    await expect(page.getByText('Nothing scheduled right now.')).toBeVisible();
    await expect(page.getByText('No blocks scheduled today.')).toBeVisible();
    await expect(page.getByText('No checklists for today.')).toBeVisible();
    // Summary tiles render zeroes rather than NaN/undefined.
    await expect(page.getByText('Day progress').locator('..')).toContainText('0%');
    await expect(page.getByText('Scheduled', { exact: true }).locator('..')).toContainText('0h 0m');
    await expect(page.locator('body')).not.toContainText(/NaN|undefined|null/);
  });

  test('every route renders its empty state without errors', async ({ page }) => {
    for (const route of ROUTES) {
      await page.goto(route.path);
      await expect(page.getByRole('heading', { level: 1 }).first(), route.path).toHaveText(route.heading);
      await expect(page.locator('body'), route.path).not.toContainText(/NaN|undefined|\[object Object\]/);
    }
    await page.goto('/checklists');
    await expect(page.getByText('No checklists yet.')).toBeVisible();
    await page.goto('/timetable');
    await expect(page.getByText('No templates yet.')).toBeVisible();
    await page.goto('/timetable/schedule');
    await expect(page.getByText('No assignments yet.')).toBeVisible();
    await page.goto('/upcoming');
    await expect(page.getByText('Nothing upcoming.')).toBeVisible();
    await page.goto('/tasks');
    await expect(page.getByText('No tasks yet — add your first one above.')).toBeVisible();
    await page.goto('/expenses');
    await expect(page.getByText('No constant expenses yet — add your first one above.')).toBeVisible();
  });

  test('notifications panel shows its empty state', async ({ page }) => {
    await page.goto('/today');
    await page.getByTitle('Notifications').click();
    await expect(page.getByText('No notifications yet.')).toBeVisible();
  });
});
