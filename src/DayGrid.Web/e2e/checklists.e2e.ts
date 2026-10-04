import { apiGet, uid } from './support/data';
import { expect, test } from './support/fixtures';

interface TodayDto {
  checklists: { checklistId: string; items: { itemId: string; title: string; isCompleted: boolean }[] }[];
}

test.describe('Checklists', () => {
  test('create → open detail → add item via form → complete on Today → persists', async ({ page, api }) => {
    const name = uid('Evening routine');
    const itemTitle = uid('Stretch');

    // Create via the list page.
    await page.goto('/checklists');
    await page.getByRole('button', { name: '+ New checklist' }).click();
    await page.getByPlaceholder('Checklist name').fill(name);
    await page.getByPlaceholder('Description (optional)').fill('Wind down before bed');
    const created = page.waitForResponse((r) => r.url().endsWith('/api/v1/checklists') && r.request().method() === 'POST');
    await page.getByRole('button', { name: 'Create checklist' }).click();
    expect((await created).status()).toBe(201);
    await expect(page.getByRole('link', { name })).toBeVisible();

    // Open the detail page.
    await page.getByRole('link', { name }).click();
    await expect(page).toHaveURL(/\/checklists\/[0-9a-f-]{36}$/);
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
    await expect(page.getByText('Wind down before bed')).toBeVisible();
    await expect(page.getByText('No items yet — add your first one above.')).toBeVisible();

    // Add an item through the anchor + recurrence form.
    await page.getByRole('button', { name: '+ Add item' }).click();
    const form = page.locator('app-checklist-item-form');
    await form.getByPlaceholder('Item title').fill(itemTitle);
    await form.locator('select[name="priority"]').selectOption('High');
    await form.getByRole('button', { name: 'FixedTime' }).click();
    await form.locator('input[name="anchorTime"]').fill('21:30');
    await form.locator('select[name="recurrenceType"]').selectOption('Daily');
    await expect(form.locator('input[name="interval"]')).toHaveValue('1');
    const itemCreated = page.waitForResponse((r) => r.url().includes('/items') && r.request().method() === 'POST');
    await form.getByRole('button', { name: 'Add item' }).click();
    expect((await itemCreated).status()).toBe(201);
    await expect(form).toHaveCount(0);
    const itemRow = page.locator('div.flex', { hasText: itemTitle }).last();
    await expect(itemRow).toContainText('21:30');
    await expect(itemRow).toContainText('Every day');

    // Deep link refresh of the detail page keeps the item.
    await page.reload();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(name);
    await expect(page.getByText(itemTitle)).toBeVisible();

    // Edit the item via the same form.
    await page.locator('div.flex', { hasText: itemTitle }).last().getByRole('button', { name: 'Edit' }).click();
    await expect(form.getByPlaceholder('Item title')).toHaveValue(itemTitle);
    await expect(form.locator('input[name="anchorTime"]')).toHaveValue('21:30');
    await form.locator('textarea[name="notes"]').fill('10 minutes');
    const itemUpdated = page.waitForResponse((r) => r.url().includes('/api/v1/items/') && r.request().method() === 'PUT');
    await form.getByRole('button', { name: 'Save item' }).click();
    expect((await itemUpdated).ok()).toBeTruthy();
    await expect(form).toHaveCount(0);

    // It shows up on Today; complete it there.
    await page.locator('aside').getByRole('link', { name: 'Today', exact: true }).click();
    const group = page.locator('div.border-b', { has: page.getByText(name, { exact: true }) }).first();
    await expect(group).toBeVisible();
    const item = group.locator('div.cursor-pointer', { hasText: itemTitle });
    await expect(item).toContainText('21:30');
    await expect(item).toContainText('↻');
    await expect(group).toContainText('0/1');

    const completed = page.waitForResponse((r) => r.url().includes('/complete') && r.request().method() === 'POST');
    await item.click();
    expect((await completed).ok()).toBeTruthy();
    await expect(item.locator('b')).toHaveClass(/line-through/);
    await expect(group).toContainText('1/1');

    const today = await apiGet<TodayDto>(api, '/api/v1/today');
    const serverItem = today.checklists.flatMap((c) => c.items).find((i) => i.title === itemTitle);
    expect(serverItem?.isCompleted).toBe(true);

    await page.reload();
    const groupAfter = page.locator('div.border-b', { has: page.getByText(name, { exact: true }) }).first();
    await expect(groupAfter.locator('div.cursor-pointer', { hasText: itemTitle }).locator('b')).toHaveClass(/line-through/);
    await expect(groupAfter).toContainText('1/1');

    // Un-complete it again.
    const uncompleted = page.waitForResponse((r) => r.url().includes('/complete') && r.request().method() === 'DELETE');
    await groupAfter.locator('div.cursor-pointer', { hasText: itemTitle }).click();
    expect((await uncompleted).ok()).toBeTruthy();
    await expect(groupAfter.locator('div.cursor-pointer', { hasText: itemTitle }).locator('b')).not.toHaveClass(/line-through/);
    await expect(groupAfter).toContainText('0/1');
  });

  test('deactivate item, archive / unarchive and delete checklist', async ({ page }) => {
    const name = uid('Archive me');
    const itemTitle = uid('Water plants');
    await page.goto('/checklists');
    await page.getByRole('button', { name: '+ New checklist' }).click();
    await page.getByPlaceholder('Checklist name').fill(name);
    await page.getByPlaceholder('Checklist name').press('Enter');
    await page.getByRole('link', { name }).click();

    await page.getByRole('button', { name: '+ Add item' }).click();
    await page.getByPlaceholder('Item title').fill(itemTitle);
    await page.locator('app-checklist-item-form').getByRole('button', { name: 'Add item' }).click();
    const itemRow = page.locator('div.flex', { hasText: itemTitle }).last();
    await expect(itemRow).toContainText('One-off');

    await itemRow.getByRole('button', { name: 'Deactivate' }).click();
    await expect(itemRow).toContainText('Inactive');
    await itemRow.getByRole('button', { name: 'Activate' }).click();
    await expect(itemRow).not.toContainText('Inactive');

    await itemRow.getByRole('button', { name: 'Delete' }).click();
    await expect(page.getByText(itemTitle)).toHaveCount(0);

    // Header edit.
    await page.getByRole('button', { name: 'Edit', exact: true }).click();
    await page.locator('input[name="headerName"]').fill(`${name} v2`);
    await page.getByRole('button', { name: 'Save', exact: true }).click();
    await expect(page.getByRole('heading', { level: 1 })).toHaveText(`${name} v2`);

    await page.getByRole('button', { name: 'Archive', exact: true }).click();
    await expect(page.getByText('Archived', { exact: true })).toBeVisible();

    await page.getByRole('link', { name: '← Checklists' }).click();
    await expect(page.getByRole('link', { name: `${name} v2` })).toHaveCount(0);
    await page.getByLabel('Show archived').check();
    const li = page.locator('li', { hasText: `${name} v2` });
    await expect(li).toBeVisible();
    await li.getByRole('button', { name: 'Unarchive' }).click();
    await expect(li.getByText('Archived', { exact: true })).toHaveCount(0);
    await li.getByRole('button', { name: 'Delete' }).click();
    await expect(page.locator('li', { hasText: `${name} v2` })).toHaveCount(0);
  });

  test('unknown checklist id shows "not found" (expected 404)', async ({ page, guard }) => {
    guard.allowApiError('/api/v1/checklists/00000000-0000-0000-0000-000000000000', 404);
    guard.allowConsoleError(/404/);
    await page.goto('/checklists/00000000-0000-0000-0000-000000000000');
    await expect(page.getByText('Checklist not found.')).toBeVisible();
  });
});
