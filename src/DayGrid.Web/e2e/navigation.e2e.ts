import { expect, test } from './support/fixtures';
import { ROUTES } from './support/routes';

test.describe('routing & shell', () => {
  for (const route of ROUTES) {
    test(`deep link ${route.path} loads and survives a refresh`, async ({ page }) => {
      const res = await page.goto(route.path);
      // SPA fallback: the server answers the deep link itself with index.html.
      expect(res?.status()).toBe(200);
      expect(res?.headers()['content-type']).toContain('text/html');
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(route.heading);
      await expect(page).toHaveURL(new RegExp(`${route.path.replace(/\//g, '\\/')}$`));

      await page.reload();
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(route.heading);
      await expect(page).toHaveURL(new RegExp(`${route.path.replace(/\//g, '\\/')}$`));
    });
  }

  test('sidebar navigation reaches every section and highlights the active link', async ({ page }) => {
    await page.goto('/today');
    const sidebar = page.locator('aside');
    for (const route of ROUTES.filter((r) => r.nav)) {
      const link = sidebar.getByRole('link', { name: route.nav!, exact: true });
      await link.click();
      await expect(page).toHaveURL(new RegExp(`${route.path}$`));
      await expect(page.getByRole('heading', { level: 1 }).first()).toHaveText(route.heading);
      await expect(link).toHaveClass(/text-accent/);
    }
    // Browser history works through the client-side router.
    await page.goBack();
    await expect(page).toHaveURL(/\/insights$/);
    await page.goForward();
    await expect(page).toHaveURL(/\/settings$/);
  });

  test('root and unknown URLs redirect to Today', async ({ page }) => {
    await page.goto('/');
    await expect(page).toHaveURL(/\/today$/);
    await page.goto('/definitely/not/a/page');
    await expect(page).toHaveURL(/\/today$/);
    await expect(page.getByRole('heading', { level: 1 })).toBeVisible();
  });

  test('today/:date deep link renders that date', async ({ page }) => {
    await page.goto('/today/2030-01-15');
    await expect(page.getByRole('heading', { level: 1 })).toContainText("15 January 2030");
    await page.reload();
    await expect(page.getByRole('heading', { level: 1 })).toContainText("15 January 2030");
  });

  test('static assets and API 404s behave', async ({ page, api }) => {
    await page.goto('/today');
    for (const asset of ['/favicon.ico', '/assets/brand/logo-tile.svg']) {
      const res = await api.get(asset);
      expect(res.status(), asset).toBe(200);
    }
    // Unknown API routes are not swallowed by the SPA fallback as HTML 200s.
    const res = await api.get('/api/v1/does-not-exist');
    expect(res.status()).toBe(404);
    expect(res.headers()['content-type'] ?? '').not.toContain('text/html');
  });

  test('theme toggle switches theme, survives navigation and a reload', async ({ page }) => {
    await page.goto('/today');
    const html = page.locator('html');
    const toggle = page.getByTitle('Toggle theme');
    await expect(html).toHaveAttribute('data-theme', 'dark');
    await expect(toggle).toHaveText('☾');
    const bodyBg = () => page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    const darkBg = await bodyBg();

    await toggle.click();
    await expect(html).toHaveAttribute('data-theme', 'light');
    await expect(toggle).toHaveText('☀');
    await expect.poll(bodyBg).not.toBe(darkBg);

    await page.locator('aside').getByRole('link', { name: 'Tasks', exact: true }).click();
    await expect(html).toHaveAttribute('data-theme', 'light');

    await page.reload();
    await expect(html).toHaveAttribute('data-theme', 'light');
    await expect(page.getByTitle('Toggle theme')).toHaveText('☀');
    // The light palette is really applied after the reload, not just the attribute.
    await expect.poll(bodyBg).not.toBe(darkBg);

    await page.getByTitle('Toggle theme').click();
    await expect(html).toHaveAttribute('data-theme', 'dark');
    await page.reload();
    await expect(html).toHaveAttribute('data-theme', 'dark');
  });
});
