import { appDate, uid, windowAroundNow } from './support/data';
import { expect, test } from './support/fixtures';

test.describe('Timetable', () => {
  test('create template → add block via form → assign to today → Today timeline + now-card', async ({ page, guard }) => {
    const name = uid('Workday');
    const blockTitle = uid('Focus block');
    const window = windowAroundNow() ?? { start: '10:00', end: '11:00' };
    const spansNow = windowAroundNow() !== null;

    // Create the template.
    await page.goto('/timetable');
    await page.getByRole('button', { name: '+ New template' }).click();
    await page.getByPlaceholder('Template name').fill(name);
    await page.locator('input[name="draftDayStart"]').fill('00:00');
    await page.locator('input[name="draftDayEnd"]').fill('23:59');
    const created = page.waitForResponse((r) => r.url().endsWith('/api/v1/timetable/templates') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create template' }).click();
    expect((await created).status()).toBe(201);
    await expect(page.getByRole('link', { name })).toBeVisible();

    // Detail page: add a block through the block form.
    await page.getByRole('link', { name }).click();
    await expect(page).toHaveURL(/\/timetable\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
    await expect(page.getByText('No blocks yet — add your first one above.')).toBeVisible();
    await page.getByRole('button', { name: '+ Add block' }).click();
    const form = page.locator('app-timetable-block-form');
    await form.getByPlaceholder('Block title').fill(blockTitle);
    await form.locator('select[name="category"]').selectOption('Work');
    await form.locator('input[name="startTime"]').fill(window.start);
    await form.locator('input[name="endTime"]').fill(window.end);
    await form.getByPlaceholder('Location (optional)').fill('Office');
    const blockCreated = page.waitForResponse((r) => r.url().includes('/blocks') && r.request().method() === 'POST');
    await form.getByRole('button', { name: 'Add block' }).click();
    expect((await blockCreated).status()).toBe(201);
    await expect(form).toHaveCount(0);
    await expect(page.getByText(blockTitle)).toBeVisible();
    await expect(page.getByText(`${window.start} – ${window.end} · Work · Office`)).toBeVisible();

    // An overlapping block is rejected with a friendly message (expected 409).
    guard.allowApiError('/blocks', 409);
    guard.allowConsoleError(/409/);
    await page.getByRole('button', { name: '+ Add block' }).click();
    await form.getByPlaceholder('Block title').fill('Overlapper');
    await form.locator('input[name="startTime"]').fill(window.start);
    await form.locator('input[name="endTime"]').fill(window.end);
    await form.getByRole('button', { name: 'Add block' }).click();
    await expect(form.getByText(/Overlaps an existing block/)).toBeVisible();
    await form.getByRole('button', { name: 'Cancel' }).click();

    // Refresh keeps the block (deep link + server state).
    await page.reload();
    await expect(page.getByText(blockTitle)).toBeVisible();

    // Assign the template to today's date.
    await page.goto('/timetable/schedule');
    await page.getByRole('button', { name: '+ New assignment' }).click();
    await page.locator('select[name="draftTemplateId"]').selectOption({ label: name });
    await page.locator('select[name="draftScope"]').selectOption('SpecificDate');
    await page.locator('input[name="draftDateFrom"]').fill(appDate());
    await page.locator('input[name="draftPriority"]').fill('50');
    const assigned = page.waitForResponse((r) => r.url().endsWith('/assignments') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create assignment' }).click();
    expect((await assigned).status()).toBe(201);
    const assignmentRow = page.locator('li', { hasText: name });
    await expect(assignmentRow).toContainText(appDate());
    await expect(assignmentRow).toContainText('priority 50');

    // Today shows the template, the block on the timeline and (when it spans now) the now-card.
    await page.locator('aside').getByRole('link', { name: 'Today', exact: true }).click();
    await expect(page.getByText(`${name} template`)).toBeVisible();
    const timelineBlock = page.getByRole('listitem').filter({ hasText: blockTitle });
    await expect(timelineBlock).toBeVisible();
    await expect(timelineBlock).toContainText(`${window.start} – ${window.end} · Office`);
    if (spansNow) {
      await expect(timelineBlock).toHaveClass(/ring-2/);
      await expect(page.getByRole('heading', { level: 2 })).toHaveText(blockTitle);
      await expect(page.getByText('Remaining')).toBeVisible();
    }

    // Clean up through the UI: delete the assignment, then the template.
    await page.goto('/timetable/schedule');
    await page.locator('li', { hasText: name }).getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('li', { hasText: name })).toHaveCount(0);
    await page.goto('/timetable');
    await page.locator('li', { hasText: name }).getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('li', { hasText: name })).toHaveCount(0);
  });

  test('edit template header, set default, edit and delete a block', async ({ page }) => {
    const name = uid('Weekend');
    await page.goto('/timetable');
    await page.getByRole('button', { name: '+ New template' }).click();
    await page.getByPlaceholder('Template name').fill(name);
    await page.getByRole('button', { name: 'Create template' }).click();
    await page.getByRole('link', { name }).click();

    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    await page.locator('input[name="headerName"]').fill(`${name} v2`);
    await page.locator('input[name="headerSlotMinutes"]').fill('15');
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(`${name} v2`);
    await expect(page.getByText(/15min slots/)).toBeVisible();

    await page.getByRole('button', { name: 'Set as default' }).click();
    await expect(page.getByText('Default', { exact: true })).toBeVisible();

    await page.getByRole('button', { name: 'Add block' }).first().click();
    const form = page.locator('app-timetable-block-form');
    await form.getByPlaceholder('Block title').fill('Brunch');
    await form.locator('input[name="startTime"]').fill('11:00');
    await form.locator('input[name="endTime"]').fill('12:00');
    await form.getByRole('button', { name: 'Add block' }).click();
    await expect(page.getByText('Brunch')).toBeVisible();

    const blockRow = page.locator('div.flex', { hasText: 'Brunch' }).last();
    await blockRow.getByRole('button', { name: 'Edit' }).click();
    await expect(form.locator('input[name="startTime"]')).toHaveValue('11:00');
    await form.getByPlaceholder('Block title').fill('Late brunch');
    await form.locator('input[name="endTime"]').fill('12:30');
    await form.getByRole('button', { name: 'Save block' }).click();
    await expect(page.getByText('11:00 – 12:30')).toBeVisible();

    await page.locator('div.flex', { hasText: 'Late brunch' }).last().getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText('Late brunch')).toHaveCount(0);

    await page.getByRole('button', { name: 'Delete', exact: true }).click();
    await expect(page).toHaveURL(/\/timetable$/);
    await expect(page.getByRole('link', { name: `${name} v2` })).toHaveCount(0);
  });
});
