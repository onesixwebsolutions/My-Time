import { APIRequestContext, Browser, BrowserContext, Page, expect, request as pwRequest } from '@playwright/test';
import * as fs from 'node:fs';

import { countEmails, waitForLink } from './email';
import { E2E_PASSWORD, E2eState, STATE_FILE } from './env';

// Account helpers for the e2e suite: XSRF-aware API contexts, registering/confirming users
// through the real endpoints + the pickup-directory emails, and signing in through the UI.

export function readState(): E2eState {
  return JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')) as E2eState;
}

/**
 * The auth rate limiter (10 auth requests / minute) partitions by client IP, and the API trusts
 * X-Forwarded-For (it runs behind App Service's front ends). Each test context gets its own
 * made-up client IP so the production limit stays in force without tests sharing a budget.
 */
export function randomIp(): string {
  const b = () => 1 + Math.floor(Math.random() * 254);
  return `10.${b()}.${b()}.${b()}`;
}

const UNSAFE = new Set(['post', 'put', 'patch', 'delete', 'fetch']);

/**
 * Wraps a request context so every unsafe call carries X-XSRF-TOKEN = the current XSRF-TOKEN
 * cookie (the server re-issues it on login/logout, so it is read per call), like the SPA does.
 */
export function withXsrf(ctx: APIRequestContext): APIRequestContext {
  return new Proxy(ctx, {
    get(target, prop, receiver) {
      const value = Reflect.get(target, prop, receiver);
      if (typeof value !== 'function') return value;
      if (typeof prop === 'string' && UNSAFE.has(prop)) {
        return async (url: string, options: Record<string, unknown> = {}) => {
          const token = (await target.storageState()).cookies.find((c) => c.name === 'XSRF-TOKEN')?.value;
          const headers = { ...((options['headers'] as Record<string, string>) ?? {}), ...(token ? { 'X-XSRF-TOKEN': token } : {}) };
          return value.call(target, url, { ...options, headers });
        };
      }
      return value.bind(target);
    }
  });
}

export interface ApiContextOptions {
  storageState?: string;
  ip?: string;
}

/** A same-origin request context (own cookie jar, own client IP) with the XSRF header handled. */
export async function newApiContext(baseURL: string, options: ApiContextOptions = {}): Promise<APIRequestContext> {
  const ctx = await pwRequest.newContext({
    baseURL,
    ignoreHTTPSErrors: true,
    storageState: options.storageState,
    extraHTTPHeaders: { 'X-Forwarded-For': options.ip ?? randomIp() }
  });
  const api = withXsrf(ctx);
  const me = await api.get('/api/v1/auth/me'); // issues XSRF-TOKEN (200 or 401)
  expect([200, 401]).toContain(me.status());
  return api;
}

export interface TestUser {
  email: string;
  password: string;
  displayName: string;
}

export function newUserData(prefix = 'user'): TestUser {
  const id = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 7)}`;
  return { email: `${prefix}-${id}@e2e.test`, password: E2E_PASSWORD, displayName: `${prefix[0].toUpperCase()}${prefix.slice(1)} ${id.slice(-4)}` };
}

/** POST /auth/register (no confirmation). */
export async function registerUser(baseURL: string, user: TestUser): Promise<void> {
  const api = await newApiContext(baseURL);
  try {
    const res = await api.post('/api/v1/auth/register', { data: user });
    expect(res.status(), await res.text()).toBe(202);
  } finally {
    await api.dispose();
  }
}

/**
 * The parameters of an emailed account link. They travel in the URL fragment
 * (`/confirm-email#userId=..&token=..`) so the token never reaches a server log.
 */
export function linkParams(link: string): URLSearchParams {
  return new URLSearchParams(new URL(link).hash.replace(/^#/, ''));
}

/** Confirms an account by POSTing the userId/token from a /confirm-email link plus its password. */
export async function confirmFromLink(baseURL: string, link: string, password: string): Promise<void> {
  const params = linkParams(link);
  const api = await newApiContext(baseURL);
  try {
    const res = await api.post('/api/v1/auth/confirm-email', {
      data: { userId: params.get('userId'), token: params.get('token'), password }
    });
    expect(res.status(), await res.text()).toBe(204);
  } finally {
    await api.dispose();
  }
}

/** Registers a brand-new account and confirms it via the link in its .eml. */
export async function createConfirmedUser(prefix = 'user', state: E2eState = readState()): Promise<TestUser> {
  const user = newUserData(prefix);
  const before = countEmails(state.pickupDir);
  await registerUser(state.baseURL, user);
  const link = await waitForLink(state.pickupDir, user.email, '/confirm-email', { since: before });
  await confirmFromLink(state.baseURL, link, user.password);
  return user;
}

/** A request context signed in as `user`. */
export async function signedInApi(user: Pick<TestUser, 'email' | 'password'>, state: E2eState = readState()): Promise<APIRequestContext> {
  const api = await newApiContext(state.baseURL);
  const res = await api.post('/api/v1/auth/login', { data: { email: user.email, password: user.password, rememberMe: false } });
  expect(res.status(), await res.text()).toBe(200);
  return api;
}

/** A fresh, signed-out browser context (own cookies and client IP) on the app's origin. */
export async function newBrowserContext(browser: Browser, state: E2eState = readState()): Promise<BrowserContext> {
  return browser.newContext({
    baseURL: state.baseURL,
    ignoreHTTPSErrors: true,
    storageState: { cookies: [], origins: [] },
    extraHTTPHeaders: { 'X-Forwarded-For': randomIp() },
    timezoneId: 'Asia/Kolkata',
    locale: 'en-IN',
    viewport: { width: 1366, height: 900 }
  });
}

/**
 * Opens an emailed /confirm-email link in the browser, checks the token was stripped from the
 * address bar, enters the account password and waits for "Email confirmed".
 */
export async function confirmViaUi(page: Page, link: string, password: string): Promise<void> {
  await page.goto(link);
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Confirm your email');
  expect(page.url()).not.toContain('token');
  await page.locator('#confirm-password').fill(password);
  await page.getByRole('button', { name: 'Confirm email' }).click();
  await expect(page.getByRole('heading', { level: 1 })).toHaveText('Email confirmed');
}

/** Fills and submits the login form (the page must be on /login). */
export async function submitLogin(page: Page, email: string, password: string): Promise<void> {
  await page.locator('#login-email').fill(email);
  await page.locator('#login-password').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
}

/** Goes to /login and signs in; waits until the app shell is showing. */
export async function signInViaUi(page: Page, email: string, password: string): Promise<void> {
  await page.goto('/login');
  await submitLogin(page, email, password);
  await expect(page.locator('button[aria-haspopup="menu"]')).toBeVisible();
}

/** Signs out through the user menu. */
export async function signOutViaUi(page: Page): Promise<void> {
  await page.locator('button[aria-haspopup="menu"]').click();
  await page.getByRole('menuitem', { name: 'Sign out' }).click();
  await expect(page).toHaveURL(/\/login(\?|$)/);
}
