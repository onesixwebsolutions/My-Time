import { Page } from '@playwright/test';

import { newBrowserContext } from './support/auth';
import { expect, test } from './support/fixtures';
import { ROUTES } from './support/routes';

// The server sends a strict CSP (script-src 'self', no inline script). Every page — public auth
// pages signed out, app pages, /account and /admin/users signed in as Admin — must render
// without a single CSP violation. Violations are caught twice: the shared guard fails on the
// console's "Refused to ..." errors, and a securitypolicyviolation listener records them.

declare global {
  interface Window {
    __cspViolations?: string[];
  }
}

async function recordViolations(page: Page): Promise<void> {
  await page.addInitScript(() => {
    window.__cspViolations = [];
    document.addEventListener('securitypolicyviolation', (e) => {
      window.__cspViolations!.push(`${e.violatedDirective} blocked ${e.blockedURI || '(inline)'} at ${e.sourceFile}:${e.lineNumber}`);
    });
  });
}

async function expectNoViolations(page: Page, path: string): Promise<void> {
  // Give late resources (lazy chunks, fonts, images) a moment to load or be blocked.
  await page.waitForLoadState('networkidle');
  expect(await page.evaluate(() => window.__cspViolations ?? []), path).toEqual([]);
}

const APP_PAGES = [...ROUTES.map((r) => r.path), '/account', '/admin/users'];
const PUBLIC_PAGES = [
  '/login',
  '/register',
  '/register/check-email?email=someone%40e2e.test',
  '/forgot-password',
  '/reset-password?email=someone%40e2e.test&token=abc',
  '/confirm-email'
];

test('the CSP header is strict and every signed-in page renders without CSP violations', async ({ page, api }) => {
  const res = await api.get('/today');
  const csp = res.headers()['content-security-policy'] ?? '';
  expect(csp).toContain("script-src 'self'");
  expect(csp).toContain("frame-ancestors 'none'");
  expect(/script-src[^;]*'unsafe-inline'/.test(csp)).toBe(false);

  await recordViolations(page);
  for (const path of APP_PAGES) {
    await page.goto(path);
    await expect(page.getByRole('heading', { level: 1 }).first(), path).toBeVisible();
    await expectNoViolations(page, path);
  }
});

test('public auth pages render without CSP violations', async ({ browser, guard }) => {
  const context = await newBrowserContext(browser);
  const page = await context.newPage();
  guard.watch(page);
  try {
    await recordViolations(page);
    for (const path of PUBLIC_PAGES) {
      await page.goto(path);
      await expect(page.getByRole('heading', { level: 1 }).first(), path).toBeVisible();
      await expectNoViolations(page, path);
    }
  } finally {
    await context.close();
  }
});
