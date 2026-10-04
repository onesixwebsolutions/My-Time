import { Locator, Page } from '@playwright/test';

import { uid } from './support/data';
import { expect, test } from './support/fixtures';

function panel(page: Page, heading: RegExp): Locator {
  return page.locator('div.overflow-hidden', { has: page.getByRole('heading', { level: 3, name: heading }) });
}

function tile(page: Page, label: RegExp): Locator {
  return page.locator('div.rounded-card', { has: page.locator('span', { hasText: label }) }).first();
}

test.describe('Expenses', () => {
  test('constant expense: add, edit, pause/resume, delete — totals follow', async ({ page }) => {
    const name = uid('Rent');
    await page.goto('/expenses');
    const constant = panel(page, /Monthly Constant Expenses/);

    await constant.getByRole('button', { name: '+ Add' }).click();
    await constant.getByRole('button', { name: 'Save' }).click();
    await expect(constant.getByText('Name and amount are required.')).toBeVisible();

    await constant.getByPlaceholder('Name (e.g. Rent)').fill(name);
    await constant.getByPlaceholder('Amount').fill('12500.50');
    await constant.getByPlaceholder('Category (optional)').fill('Housing');
    await constant.getByPlaceholder('Due day').fill('5');
    const created = page.waitForResponse((r) => r.url().endsWith('/expenses/constant') && r.request().method() === 'POST');
    await constant.getByRole('button', { name: 'Save' }).click();
    expect((await created).status()).toBe(201);

    const row = constant.locator('div.flex', { hasText: name }).last();
    await expect(row).toContainText('₹12,500.50');
    await expect(row).toContainText('Due day 5');
    await expect(tile(page, /Monthly constant/)).toContainText('12,500.50');

    await row.getByRole('button', { name: 'Edit' }).click();
    await constant.locator('input[name="editAmount"]').fill('13000');
    await constant.getByRole('button', { name: 'Save' }).click();
    await expect(constant.locator('div.flex', { hasText: name }).last()).toContainText('₹13,000.00');

    // Pause hides it (inactive not shown by default); "Show inactive" brings it back.
    await constant.locator('div.flex', { hasText: name }).last().getByRole('button', { name: 'Pause' }).click();
    await expect(constant.getByText(name)).toHaveCount(0);
    await constant.getByLabel('Show inactive').check();
    const paused = constant.locator('div.flex', { hasText: name }).last();
    await expect(paused).toContainText('Inactive');
    await paused.getByRole('button', { name: 'Resume' }).click();
    await expect(constant.locator('div.flex', { hasText: name }).last()).not.toContainText('Inactive');

    await page.reload();
    await expect(panel(page, /Monthly Constant Expenses/).getByText(name)).toBeVisible();
    await panel(page, /Monthly Constant Expenses/).locator('div.flex', { hasText: name }).last().getByRole('button', { name: 'Delete' }).click();
    await expect(panel(page, /Monthly Constant Expenses/).getByText(name)).toHaveCount(0);
  });

  test('varying expense and my spends: add, list per month, edit, delete', async ({ page }) => {
    const title = uid('Groceries');
    const spendTitle = uid('Coffee');
    await page.goto('/expenses');

    const varying = panel(page, /Varying Expenses/);
    await varying.getByRole('button', { name: '+ Add' }).click();
    // Date defaults to the first of the viewed month.
    await expect(varying.locator('input[name="draftDate"]')).toHaveValue(/^\d{4}-\d{2}-01$/);
    await varying.getByPlaceholder('What did you spend on?').fill(title);
    await varying.getByPlaceholder('Amount').fill('842.25');
    await varying.getByPlaceholder('Category (optional)').fill('Food');
    const created = page.waitForResponse((r) => r.url().endsWith('/expenses/varying') && r.request().method() === 'POST');
    await varying.getByRole('button', { name: 'Save' }).click();
    expect((await created).status()).toBe(201);
    const row = varying.locator('div.flex', { hasText: title }).last();
    await expect(row).toContainText('₹842.25');
    await expect(tile(page, /^Varying/)).toContainText('842.25');

    await row.getByRole('button', { name: 'Edit' }).click();
    await varying.locator('input[name="editVaryingAmount"]').fill('900');
    await varying.getByRole('button', { name: 'Save' }).click();
    await expect(varying.locator('div.flex', { hasText: title }).last()).toContainText('₹900.00');

    // Month navigation: the expense is scoped to its month.
    await page.getByRole('button', { name: '‹' }).click();
    await expect(varying.getByText(title)).toHaveCount(0);
    await page.getByRole('button', { name: '›' }).click();
    await expect(varying.getByText(title)).toBeVisible();

    const spends = panel(page, /My Spends/);
    await spends.getByRole('button', { name: '+ Add' }).click();
    await spends.getByPlaceholder('What did you spend on?').fill(spendTitle);
    await spends.getByPlaceholder('Amount').fill('180');
    const spendCreated = page.waitForResponse((r) => r.url().endsWith('/expenses/spends') && r.request().method() === 'POST');
    await spends.getByRole('button', { name: 'Save' }).click();
    expect((await spendCreated).status()).toBe(201);
    await expect(spends.locator('div.flex', { hasText: spendTitle }).last()).toContainText('₹180.00');
    await expect(tile(page, /My spends completed/)).toContainText('180.00');

    await page.reload();
    await expect(panel(page, /Varying Expenses/).getByText(title)).toBeVisible();
    await expect(panel(page, /My Spends/).getByText(spendTitle)).toBeVisible();

    await panel(page, /Varying Expenses/).locator('div.flex', { hasText: title }).last().getByRole('button', { name: 'Delete' }).click();
    await expect(panel(page, /Varying Expenses/).getByText(title)).toHaveCount(0);
    await panel(page, /My Spends/).locator('div.flex', { hasText: spendTitle }).last().getByRole('button', { name: 'Delete' }).click();
    await expect(panel(page, /My Spends/).getByText(spendTitle)).toHaveCount(0);
  });
});
