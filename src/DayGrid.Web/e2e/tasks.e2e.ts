import { Page } from '@playwright/test';

import { apiGet, uid } from './support/data';
import { expect, test } from './support/fixtures';

interface SimpleTask {
  id: string;
  title: string;
  status: 'Open' | 'Done';
  priority: string;
  notes: string | null;
}

function row(page: Page, title: string) {
  return page.locator('app-task-row').filter({ hasText: title });
}

test.describe('Tasks', () => {
  test('quick-add (Enter and button), toggle done, edit, delete — persisted to the API', async ({ page, api }) => {
    await page.goto('/tasks');
    const input = page.getByPlaceholder('Add a task and press Enter…');
    const first = uid('Buy milk');
    const second = uid('Call plumber');

    // Quick-add with Enter.
    let created = page.waitForResponse((r) => r.url().endsWith('/api/v1/tasks') && r.request().method() === 'POST');
    await input.fill(first);
    await input.press('Enter');
    expect((await created).status()).toBe(201);
    await expect(row(page, first)).toBeVisible();
    await expect(input).toHaveValue('');

    // Quick-add with the button.
    created = page.waitForResponse((r) => r.url().endsWith('/api/v1/tasks') && r.request().method() === 'POST');
    await input.fill(second);
    await page.getByRole('button', { name: 'Add', exact: true }).click();
    expect((await created).status()).toBeLessThan(300);
    await expect(row(page, second)).toBeVisible();

    // Whitespace-only input adds nothing.
    const countBefore = await page.locator('app-task-row').count();
    await input.fill('   ');
    await input.press('Enter');
    await expect(page.locator('app-task-row')).toHaveCount(countBefore);

    // Toggle done.
    const patch = page.waitForResponse((r) => r.url().includes('/status') && r.request().method() === 'PATCH');
    await row(page, first).getByRole('button', { name: 'Mark done' }).click();
    expect((await patch).ok()).toBeTruthy();
    await expect(row(page, first).getByRole('button', { name: 'Mark open' })).toBeVisible();
    await expect(row(page, first).locator('b')).toHaveClass(/line-through/);

    // Edit title / priority / notes inline.
    const renamed = `${second} (edited)`;
    await row(page, second).hover();
    await row(page, second).getByRole('button', { name: 'Edit task' }).click();
    const editor = page.locator('app-task-row').filter({ has: page.locator('textarea') });
    await editor.locator('input').fill(renamed);
    await editor.locator('select').selectOption('High');
    await editor.locator('textarea').fill('ask about the sink');
    const put = page.waitForResponse((r) => r.url().includes('/api/v1/tasks/') && r.request().method() === 'PUT');
    await editor.getByRole('button', { name: 'Save' }).click();
    expect((await put).ok()).toBeTruthy();
    await expect(row(page, renamed)).toContainText('High');
    await expect(row(page, renamed)).toContainText('ask about the sink');

    // Survives a reload (server state, not just optimistic UI).
    await page.reload();
    await expect(row(page, first).getByRole('button', { name: 'Mark open' })).toBeVisible();
    await expect(row(page, renamed)).toContainText('High');

    const tasks = await apiGet<SimpleTask[]>(api, '/api/v1/tasks');
    expect(tasks.find((t) => t.title === first)?.status).toBe('Done');
    expect(tasks.find((t) => t.title === renamed)?.notes).toBe('ask about the sink');

    // Un-toggle, then delete both.
    await row(page, first).getByRole('button', { name: 'Mark open' }).click();
    await expect(row(page, first).getByRole('button', { name: 'Mark done' })).toBeVisible();

    for (const title of [first, renamed]) {
      const del = page.waitForResponse((r) => r.url().includes('/api/v1/tasks/') && r.request().method() === 'DELETE');
      await row(page, title).hover();
      await row(page, title).getByRole('button', { name: 'Remove task' }).click();
      expect((await del).status()).toBe(204);
      await expect(row(page, title)).toHaveCount(0);
    }
    await page.reload();
    await expect(row(page, first)).toHaveCount(0);
    await expect(row(page, renamed)).toHaveCount(0);
  });

  test('open/done counters follow the list', async ({ page }) => {
    await page.goto('/tasks');
    const title = uid('Counter check');
    const counter = page.getByText(/\d+ open · \d+ done/);
    const before = await counter.textContent();
    const [open, done] = (before ?? '').match(/\d+/g)!.map(Number);

    await page.getByPlaceholder('Add a task and press Enter…').fill(title);
    const created = page.waitForResponse((r) => r.url().endsWith('/api/v1/tasks') && r.request().method() === 'POST');
    await page.getByPlaceholder('Add a task and press Enter…').press('Enter');
    await created;
    await expect(counter).toHaveText(`${open + 1} open · ${done} done`);

    await row(page, title).getByRole('button', { name: 'Mark done' }).click();
    await expect(counter).toHaveText(`${open} open · ${done + 1} done`);

    await row(page, title).hover();
    const del = page.waitForResponse((r) => r.request().method() === 'DELETE');
    await row(page, title).getByRole('button', { name: 'Remove task' }).click();
    await del;
    await expect(counter).toHaveText(`${open} open · ${done} done`);
  });
});
