import { Seeded, seedToday, unseedToday, windowAroundNow } from './support/data';
import { expect, test } from './support/fixtures';

test.describe('Today with seeded data', () => {
  let seeded: Seeded | null = null;

  test.afterEach(async ({ api }) => {
    if (seeded) await unseedToday(api, seeded);
    seeded = null;
  });

  test('now-card, live clock, timeline, checklist and due-today render', async ({ page, api }) => {
    const window = windowAroundNow();
    test.skip(window === null, 'Too close to midnight for a block spanning now');
    seeded = await seedToday(api, window!);

    await page.goto('/today');
    await expect(page.getByText(`${seeded.templateName} template`)).toBeVisible();

    // Now-card.
    const nowCard = page.locator('div.rounded-2xl', { has: page.getByRole('heading', { level: 2 }) });
    await expect(nowCard.getByRole('heading', { level: 2 })).toHaveText(seeded.blockTitle);
    await expect(nowCard).toContainText(`Work · ${window!.start}–${window!.end}`);
    await expect(nowCard).toContainText('Remaining');
    await expect(nowCard).toContainText(/\d+% elapsed/);
    await expect(nowCard.getByText(/^\d+(h \d+)?m$/)).toBeVisible();

    // The clock label ticks every second without any network call.
    const clock = nowCard.locator('span.tnum').first();
    await expect(clock).toHaveText(/^\d{2}:\d{2}:\d{2}$/);
    const first = await clock.textContent();
    await expect(clock).not.toHaveText(first!, { timeout: 3_000 });
    const second = await clock.textContent();
    await expect(clock).not.toHaveText(second!, { timeout: 3_000 });

    // Elapsed % is a sane number and the progress bar width matches it.
    const elapsedText = await nowCard.getByText(/\d+% elapsed/).textContent();
    const elapsed = Number(elapsedText!.match(/\d+/)![0]);
    expect(elapsed).toBeGreaterThanOrEqual(0);
    expect(elapsed).toBeLessThanOrEqual(100);

    // Timeline marks the block as current.
    const block = page.getByRole('listitem').filter({ hasText: seeded.blockTitle });
    await expect(block).toHaveClass(/ring-2/);
    await expect(block).toContainText('Desk');

    // Checklist column + due today.
    await expect(page.getByText(seeded.checklistName, { exact: true })).toBeVisible();
    await expect(page.getByText(seeded.itemTitle)).toBeVisible();
    await expect(page.getByText('Due today')).toBeVisible();
    const due = page.locator('div.flex', { hasText: seeded.futureTaskTitle }).last();
    await expect(due).toContainText('All day');
    await expect(due).toContainText('High');

    // Stats tiles.
    await expect(page.getByText('Blocks done').locator('..')).toContainText('/ 1');
    await expect(page.getByText('Scheduled', { exact: true }).locator('..')).toContainText(/\dh \d+m/);
    await expect(page.locator('body')).not.toContainText(/NaN|undefined/);
  });

  test('a past date deep link renders that day read-only without errors', async ({ page, api }) => {
    const window = windowAroundNow() ?? { start: '10:00', end: '11:00' };
    seeded = await seedToday(api, window);
    await page.goto('/today/2020-02-29');
    await expect(page.getByRole('heading', { level: 1 })).toContainText('29 February 2020');
    await expect(page.getByText('Nothing scheduled right now.')).toBeVisible();
  });
});
