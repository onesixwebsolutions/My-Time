import { apiGet } from './support/data';
import { expect, test } from './support/fixtures';

interface AppSettings {
  timeZone: string;
  weekStartsOn: string | number;
  dayStart: string;
  dayEnd: string;
  defaultSlotMinutes: number;
  emailEnabled: boolean;
  emailTo: string | null;
  dailyDigestTime: string | null;
  theme: string;
}

// The Settings page is intentionally a read-only view of app_settings until Phase 6/7 (it has
// no form), so "save" goes through PUT /api/v1/settings and the page must reflect it on reload.
test.describe('Settings', () => {
  test('loads the app_settings row', async ({ page }) => {
    await page.goto('/settings');
    await expect(page.getByText('Coming in Phase 6 / Phase 7.')).toBeVisible();
    const dl = page.locator('dl');
    await expect(dl).toContainText('Time zone');
    await expect(dl).toContainText('Asia/Kolkata');
    await expect(dl).toContainText(/\d{2}:\d{2}(:\d{2})? – \d{2}:\d{2}/);
    await expect(dl).toContainText(/\d+ min/);
    await expect(dl).toContainText(/Enabled|Disabled/);
  });

  test('saved settings persist and show after reload', async ({ page, api }) => {
    const original = await apiGet<AppSettings>(api, '/api/v1/settings');
    try {
      const res = await api.put('/api/v1/settings', {
        data: { ...original, defaultSlotMinutes: 45, dayStart: '07:15:00', theme: 'light' }
      });
      expect(res.status(), await res.text()).toBe(200);

      await page.goto('/settings');
      await expect(page.locator('dl')).toContainText('45 min');
      await expect(page.locator('dl')).toContainText('07:15');
      await page.reload();
      await expect(page.locator('dl')).toContainText('45 min');
      await expect(page.locator('dl')).toContainText('light');
    } finally {
      const restore = await api.put('/api/v1/settings', { data: original });
      expect(restore.status()).toBe(200);
    }
    await page.reload();
    await expect(page.locator('dl')).toContainText(`${original.defaultSlotMinutes} min`);
  });
});
