import { appDate, apiGet, uid } from './support/data';
import { expect, test } from './support/fixtures';

interface FutureTask {
  id: string;
  title: string;
  status: string;
  dueDate: string;
  reminders: { offsetMinutes: number; channels: string; status: string }[];
}

test.describe('Upcoming', () => {
  test('create future task with reminder → appears → mark done → reopen → edit → defer → delete', async ({ page, api }) => {
    const title = uid('Renew passport');
    const due = appDate(3);

    await page.goto('/upcoming');
    await page.getByRole('button', { name: '+ New task' }).click();

    // Validation: title + due date are required (no request is sent).
    await page.getByRole('button', { name: 'Create task' }).click();
    await expect(page.getByText('Title and due date are required.')).toBeVisible();

    const fields = page.locator('app-future-task-fields');
    await fields.getByPlaceholder('Task title').fill(title);
    await fields.locator('select[name="ftPriority"]').selectOption('High');
    await fields.getByPlaceholder('Notes (optional)').fill('Bring photos');
    await fields.locator('input[name="ftDueDate"]').fill(due);
    await fields.locator('input[name="ftDueTime"]').fill('10:30');
    await fields.getByPlaceholder('Category (optional)').fill('Errands');

    // Reminder: 60 minutes before, in-app.
    await page.getByPlaceholder('minutes before').fill('60');
    await page.getByRole('button', { name: '+ Add reminder' }).click();
    await expect(page.getByText('1h before · InApp')).toBeVisible();

    const created = page.waitForResponse((r) => r.url().endsWith('/api/v1/future-tasks') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create task' }).click();
    expect((await created).status()).toBe(201);

    const row = page.locator('li', { hasText: title });
    await expect(row).toBeVisible();
    await expect(row).toContainText(`${due} · 10:30`);
    await expect(row).toContainText('Errands');
    await expect(row).toContainText('1 reminder');
    await expect(row).toContainText('Pending');

    const server = (await apiGet<FutureTask[]>(api, '/api/v1/future-tasks')).find((t) => t.title === title)!;
    expect(server.reminders).toHaveLength(1);
    expect(server.reminders[0].offsetMinutes).toBe(60);
    expect(server.reminders[0].status).toBe('Scheduled');

    // Status filter.
    await page.getByRole('button', { name: 'Pending', exact: true }).click();
    await expect(row).toBeVisible();
    await page.getByRole('button', { name: 'Done', exact: true }).first().click();
    await expect(page.locator('li', { hasText: title })).toHaveCount(0);
    await page.getByRole('button', { name: 'All', exact: true }).click();
    await expect(row).toBeVisible();

    // Mark done.
    await row.getByRole('button', { name: 'Done' }).click();
    await expect(row).toContainText('Done');
    await expect(row.getByRole('button', { name: 'Reopen' })).toBeVisible();
    await expect(row.locator('b')).toHaveClass(/line-through/);
    await page.reload();
    await expect(page.locator('li', { hasText: title })).toContainText('Done');

    // Reopen.
    await page.locator('li', { hasText: title }).getByRole('button', { name: 'Reopen' }).click();
    await expect(page.locator('li', { hasText: title })).toContainText('Pending');

    // Edit: rename + add a second reminder.
    await page.locator('li', { hasText: title }).getByRole('button', { name: 'Edit' }).click();
    const editor = page.locator('li', { has: page.locator('app-future-task-fields') });
    await expect(editor.getByText('1h before')).toBeVisible();
    await editor.getByPlaceholder('minutes before').fill('1440');
    const reminderAdded = page.waitForResponse((r) => r.url().includes('/reminders') && r.request().method() === 'POST');
    await editor.getByRole('button', { name: '+ Add reminder' }).click();
    expect((await reminderAdded).status()).toBeLessThan(300);
    await expect(editor.getByText(/1d before/)).toBeVisible();
    await editor.getByPlaceholder('Task title').fill(`${title} (edited)`);
    await editor.getByRole('button', { name: 'Save' }).click();
    const edited = page.locator('li', { hasText: `${title} (edited)` });
    await expect(edited).toContainText('2 reminders');

    // Defer via the prompt.
    const newDue = appDate(10);
    page.once('dialog', (dialog) => dialog.accept(newDue));
    await edited.getByRole('button', { name: 'Defer' }).click();
    await expect(edited).toContainText(newDue);
    await expect(edited).toContainText('Deferred');

    // Delete.
    await edited.getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('li', { hasText: title })).toHaveCount(0);
    await page.reload();
    await expect(page.locator('li', { hasText: title })).toHaveCount(0);
  });
});

test.describe('Calendar', () => {
  test('month grid, add a reminder on a day, mark done, delete', async ({ page }) => {
    const title = uid('Dentist');
    await page.goto('/calendar');
    await expect(page.getByRole('heading', { level: 1 })).toHaveText('Calendar');
    const monthLabel = page.locator('span.w-\\[140px\\]');
    const initialMonth = await monthLabel.textContent();

    await page.getByRole('button', { name: '›' }).click();
    await expect(monthLabel).not.toHaveText(initialMonth!);
    await page.getByRole('button', { name: '‹' }).click();
    await expect(monthLabel).toHaveText(initialMonth!);

    await page.getByRole('button', { name: 'Today', exact: true }).click();
    await expect(page.getByRole('heading', { level: 3 })).toBeVisible();

    // Use the 15th of next month so the reminder is always in the future.
    await page.getByRole('button', { name: '›' }).click();
    const day15 = page.locator('button.min-h-\\[84px\\]:not(.opacity-40)').filter({ has: page.locator('span', { hasText: /^\s*15\s*$/ }) });
    await day15.click();
    await expect(page.getByRole('heading', { level: 3 })).toContainText('15');
    await page.getByRole('button', { name: '+ Add reminder' }).click();
    await page.getByPlaceholder('What should we remind you about?').fill(title);
    await page.locator('input[name="draftTime"]').fill('23:30');
    const created = page.waitForResponse((r) => r.url().endsWith('/api/v1/future-tasks') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Add reminder', exact: true }).click();
    expect((await created).status()).toBe(201);

    const entry = page.locator('div.flex', { hasText: title }).last();
    await expect(entry).toContainText('23:30');
    await expect(entry).toContainText('InApp');
    // The day cell shows the task too.
    await expect(page.locator('button.min-h-\\[84px\\]', { hasText: title })).toBeVisible();

    await entry.getByRole('button', { name: 'Done' }).click();
    await expect(entry.locator('b')).toHaveClass(/line-through/);
    await entry.getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText(title)).toHaveCount(0);
  });
});
