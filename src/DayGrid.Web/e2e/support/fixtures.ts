import { APIRequestContext, ConsoleMessage, Request, Response, expect, test as base } from '@playwright/test';
import * as fs from 'node:fs';

import { E2eState, STATE_FILE } from './env';

export interface BrowserGuard {
  /** Declares an /api response >= 400 this test deliberately provokes. */
  allowApiError(urlPart: string, status: number): void;
  /** Declares a console error this test deliberately provokes. */
  allowConsoleError(pattern: RegExp): void;
  /** Problems recorded so far (for tests that want to assert mid-flow). */
  problems(): string[];
}

function resolveBaseURL(): string {
  const fromEnv = process.env['E2E_BASE_URL'];
  if (fromEnv) return fromEnv;
  const state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')) as E2eState;
  return state.baseURL;
}

/**
 * Every test gets a guard (auto fixture) that fails it on:
 *  - any browser console error,
 *  - any uncaught page error,
 *  - any /api response with status >= 400 the test didn't declare via allowApiError,
 *  - any failed request (navigation aborts excepted).
 * `api` is a request context on the same origin for seeding/verifying data.
 */
export const test = base.extend<{ guard: BrowserGuard; api: APIRequestContext }>({
  baseURL: async ({}, use) => {
    await use(resolveBaseURL());
  },

  api: async ({ playwright, baseURL }, use) => {
    const ctx = await playwright.request.newContext({ baseURL });
    await use(ctx);
    await ctx.dispose();
  },

  guard: [
    async ({ page }, use, testInfo) => {
      const problems: string[] = [];
      const allowedApi: { urlPart: string; status: number }[] = [];
      const allowedConsole: RegExp[] = [];

      const onConsole = (msg: ConsoleMessage) => {
        if (msg.type() !== 'error') return;
        const text = msg.text();
        if (allowedConsole.some((re) => re.test(text))) return;
        const loc = msg.location();
        problems.push(`console.error: ${text}${loc?.url ? ` (${loc.url}:${loc.lineNumber})` : ''}`);
      };
      const onPageError = (err: Error) => problems.push(`pageerror: ${err.message}\n${err.stack ?? ''}`);
      const onResponse = (res: Response) => {
        const url = res.url();
        if (!url.includes('/api/') || res.status() < 400) return;
        const idx = allowedApi.findIndex((a) => url.includes(a.urlPart) && a.status === res.status());
        if (idx >= 0) return;
        problems.push(`api ${res.request().method()} ${url} -> ${res.status()}`);
      };
      const onRequestFailed = (req: Request) => {
        const errorText = req.failure()?.errorText ?? 'unknown';
        // A request still in flight when the page navigates/reloads is aborted by the browser.
        if (errorText === 'net::ERR_ABORTED') return;
        problems.push(`requestfailed: ${req.method()} ${req.url()} (${errorText})`);
      };

      page.on('console', onConsole);
      page.on('pageerror', onPageError);
      page.on('response', onResponse);
      page.on('requestfailed', onRequestFailed);

      await use({
        allowApiError: (urlPart, status) => allowedApi.push({ urlPart, status }),
        allowConsoleError: (pattern) => allowedConsole.push(pattern),
        problems: () => [...problems]
      });

      page.off('console', onConsole);
      page.off('pageerror', onPageError);
      page.off('response', onResponse);
      page.off('requestfailed', onRequestFailed);

      if (problems.length) {
        await testInfo.attach('browser-problems', { body: problems.join('\n\n'), contentType: 'text/plain' });
      }
      expect(problems, 'browser console errors / page errors / failed or unexpected API requests').toEqual([]);
    },
    { auto: true }
  ]
});

export { expect };
