import { Page } from '@playwright/test';

import { expect, test } from './support/fixtures';
import { ROUTES } from './support/routes';

// 390×844 (iPhone 12-class) sanity: every main page renders, nothing forces the document to
// scroll sideways, and every primary navigation link can be reached and used.

async function horizontalOverflow(page: Page): Promise<{ scrollWidth: number; clientWidth: number; offenders: string[] }> {
  return page.evaluate(() => {
    const doc = document.documentElement;
    const offenders: string[] = [];
    for (const el of Array.from(document.querySelectorAll<HTMLElement>('body *'))) {
      const r = el.getBoundingClientRect();
      if (r.width > 0 && r.right > doc.clientWidth + 1) {
        offenders.push(`${el.tagName.toLowerCase()}.${String(el.className).slice(0, 60)} right=${Math.round(r.right)}`);
        if (offenders.length >= 5) break;
      }
    }
    return { scrollWidth: doc.scrollWidth, clientWidth: doc.clientWidth, offenders };
  });
}

test.describe('mobile viewport', () => {
  for (const route of ROUTES) {
    test(`${route.path} has no horizontal scroll`, async ({ page }) => {
      await page.goto(route.path);
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(route.heading);
      const o = await horizontalOverflow(page);
      expect(o.scrollWidth, `document wider than viewport: ${o.offenders.join(' | ')}`).toBeLessThanOrEqual(o.clientWidth);
    });
  }

  test('navigation is reachable and usable', async ({ page }) => {
    await page.goto('/today');
    // Wait for the shell: before it renders, isVisible() below is false and the menu-toggle
    // fallback would open the user menu (whose backdrop then swallows the clicks).
    await expect(page.getByRole('heading', { level: 1 }).first()).toBeVisible();
    for (const route of ROUTES.filter((r) => r.nav)) {
      const link = page.getByRole('link', { name: route.nav!, exact: true });
      // Open a navigation menu first if the layout collapses the sidebar behind a toggle.
      if (!(await link.isVisible())) await page.getByRole('button', { name: /menu|navigation/i }).click();
      await expect(link).toBeVisible();
      // The nav bar scrolls horizontally inside itself; bring the link into view first.
      await link.scrollIntoViewIfNeeded();
      const box = await link.boundingBox();
      expect(box, `${route.nav} link has a box`).not.toBeNull();
      expect(box!.x).toBeGreaterThanOrEqual(-1); // sub-pixel tolerance
      expect(box!.x + box!.width).toBeLessThanOrEqual(391);
      await link.click();
      await expect(page).toHaveURL(new RegExp(`${route.path}$`));
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(route.heading);
    }
  });

  test('theme toggle and notifications are reachable', async ({ page }) => {
    await page.goto('/today');
    const toggle = page.getByTitle('Toggle theme');
    await expect(toggle).toBeInViewport();
    await expect(page.getByTitle('Notifications')).toBeInViewport();
    await page.getByTitle('Notifications').click();
    await expect(page.getByText('Notifications', { exact: true })).toBeVisible();
    const o = await horizontalOverflow(page);
    expect(o.scrollWidth, `notification panel overflows: ${o.offenders.join(' | ')}`).toBeLessThanOrEqual(o.clientWidth);
  });
});
