import { defineConfig, devices } from '@playwright/test';

import { STORAGE_STATE } from './support/env';

// End-to-end tests against the real stack: the published DayGrid.Api serving the production
// Angular build from wwwroot, backed by a private embedded PostgreSQL in a throwaway temp dir
// (see global-setup.ts). Uses the locally installed Google Chrome — no browser download.
//
// The suite shares one database, so it runs serially (workers: 1). The "empty-db" project runs
// first, before any other spec has written data.
export default defineConfig({
  testDir: '.',
  testMatch: /.*\.e2e\.ts$/,
  outputDir: './test-results',
  fullyParallel: false,
  workers: 1,
  retries: 0,
  forbidOnly: !!process.env['CI'],
  timeout: 60_000,
  expect: { timeout: 10_000 },
  reporter: [['list'], ['html', { outputFolder: './playwright-report', open: 'never' }]],
  globalSetup: require.resolve('./global-setup'),
  globalTeardown: require.resolve('./global-teardown'),
  use: {
    channel: 'chrome',
    headless: true,
    // Production-mode server over HTTPS with the ASP.NET dev certificate (which may be untrusted).
    ignoreHTTPSErrors: true,
    // Every context starts signed in as the first account (Admin) — see global-setup.ts.
    storageState: STORAGE_STATE,
    // The API's App:TimeZone is Asia/Kolkata; keep the browser's local clock on the same zone
    // so the Today page's client-side clock and the server's "now" agree.
    timezoneId: 'Asia/Kolkata',
    locale: 'en-IN',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure'
  },
  projects: [
    {
      name: 'empty-db',
      testMatch: /empty-db\.e2e\.ts$/,
      use: { ...devices['Desktop Chrome'], channel: 'chrome' }
    },
    {
      name: 'desktop',
      testIgnore: [/empty-db\.e2e\.ts$/, /mobile\.e2e\.ts$/],
      dependencies: ['empty-db'],
      use: { ...devices['Desktop Chrome'], channel: 'chrome', viewport: { width: 1366, height: 900 } }
    },
    {
      name: 'mobile',
      testMatch: /mobile\.e2e\.ts$/,
      dependencies: ['empty-db'],
      use: {
        channel: 'chrome',
        viewport: { width: 390, height: 844 },
        deviceScaleFactor: 3,
        isMobile: true,
        hasTouch: true
      }
    }
  ]
});
