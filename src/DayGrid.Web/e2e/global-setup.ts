import { spawn, spawnSync } from 'node:child_process';
import * as crypto from 'node:crypto';
import * as fs from 'node:fs';
import * as https from 'node:https';
import * as net from 'node:net';
import * as os from 'node:os';
import * as path from 'node:path';

import { confirmFromLink, newApiContext, newUserData, registerUser } from './support/auth';
import { waitForLink } from './support/email';
import {
  API_PROJECT,
  BINARIES_CACHE,
  E2eState,
  INTEGRATION_BINARIES,
  PUBLISH_DIR,
  REPO_ROOT,
  STATE_FILE,
  STORAGE_STATE
} from './support/env';
import { killTree, removeDirWithRetry, stopEmbeddedPostgres } from './support/processes';

const READY_TIMEOUT_MS = 240_000;

/**
 * 1. `dotnet publish` the API (its MSBuild targets run the Angular production build and copy
 *    it into wwwroot) — skipped with E2E_SKIP_PUBLISH=1 when a previous publish exists.
 * 2. Start the published API on a free loopback port with Database:Mode=Embedded and a fresh
 *    temp data directory (a private PostgreSQL 16 on a free port >= 5500; never 5432 and never
 *    %LocalAppData%\DayGrid). It runs as in Production (Secure-only auth cookie, HSTS, HTTPS
 *    redirection) over HTTPS with the ASP.NET Core development certificate; emails go to a
 *    pickup folder as .eml files and App:PublicBaseUrl points at the test server so the links
 *    in them open the test app.
 * 3. Wait for /health/ready.
 * 4. Register + confirm the first account (Admin) through the API and the .eml link, and save
 *    its signed-in storageState, which every spec's browser context starts from.
 */
export default async function globalSetup(): Promise<void> {
  const exe = path.join(PUBLISH_DIR, process.platform === 'win32' ? 'DayGrid.Api.exe' : 'DayGrid.Api');
  const skipPublish = process.env['E2E_SKIP_PUBLISH'] === '1' && fs.existsSync(exe);
  if (!skipPublish) {
    console.log(`[e2e] Publishing API + SPA to ${PUBLISH_DIR} …`);
    const result = spawnSync('dotnet', ['publish', API_PROJECT, '-c', 'Release', '-o', PUBLISH_DIR, '--nologo', '-v', 'q'], {
      cwd: REPO_ROOT,
      stdio: 'inherit',
      shell: false
    });
    if (result.status !== 0) throw new Error(`dotnet publish failed with exit code ${result.status}`);
  } else {
    console.log(`[e2e] Reusing existing publish in ${PUBLISH_DIR}`);
  }
  if (!fs.existsSync(path.join(PUBLISH_DIR, 'wwwroot', 'index.html'))) {
    throw new Error(`Publish output has no wwwroot/index.html — the Angular build was not copied into ${PUBLISH_DIR}`);
  }

  const attempts = 2;
  for (let attempt = 1; attempt <= attempts; attempt++) {
    try {
      const state = await startApi(exe);
      process.env['E2E_BASE_URL'] = state.baseURL;
      state.admin = await registerAdmin(state);
      fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
      return;
    } catch (err) {
      if (attempt === attempts) throw err;
      console.warn(`[e2e] API start attempt ${attempt} failed — retrying with a fresh data dir.
${(err as Error).message}`);
    }
  }
}

/** Starts the published API against a fresh embedded-Postgres data dir and waits for readiness. */
async function startApi(exe: string): Promise<E2eState> {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), 'daygrid-e2e-data-'));
  seedPostgresBinaries(dataDir);

  const port = await freePort();
  const baseURL = `https://127.0.0.1:${port}`;
  const logFile = path.join(dataDir, 'api.log');
  const pickupDir = path.join(dataDir, 'mail');
  fs.mkdirSync(pickupDir, { recursive: true });
  const cert = exportDevCertificate(dataDir);
  const log = fs.openSync(logFile, 'a');

  const child = spawn(exe, [], {
    cwd: PUBLISH_DIR,
    stdio: ['ignore', log, log],
    detached: process.platform !== 'win32',
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Production',
      DOTNET_ENVIRONMENT: 'Production',
      ASPNETCORE_URLS: baseURL,
      Kestrel__Certificates__Default__Path: cert.path,
      Kestrel__Certificates__Default__Password: cert.password,
      Database__Mode: 'Embedded',
      Database__EmbeddedDataDir: dataDir,
      App__TimeZone: 'Asia/Kolkata',
      App__PublicBaseUrl: baseURL,
      // Every email (confirmation, reset, reminders) becomes an .eml file — never real SMTP.
      Email__Mode: 'Pickup',
      Email__PickupDirectory: pickupDir,
      Email__Host: '127.0.0.1',
      Email__Port: '9',
      Email__Password: ''
    }
  });
  fs.closeSync(log);

  const state: E2eState = { pid: child.pid!, dataDir, logFile, baseURL, pickupDir };
  // Written before waiting so globalTeardown can still clean up if setup is interrupted.
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));

  let exited: number | null = null;
  child.on('exit', (code) => (exited = code ?? -1));

  console.log(`[e2e] Starting API at ${baseURL} (data dir ${dataDir}) …`);
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (exited !== null) break;
    try {
      if ((await httpsStatus(`${baseURL}/health/ready`)) === 200) {
        console.log('[e2e] API is ready.');
        cacheBinaries(dataDir);
        return state;
      }
    } catch {
      /* not listening yet */
    }
    await new Promise((r) => setTimeout(r, 500));
  }

  const output = fs.existsSync(logFile) ? fs.readFileSync(logFile, 'utf8') : '';
  killTree(child.pid!);
  stopEmbeddedPostgres(dataDir);
  await removeDirWithRetry(dataDir);
  fs.rmSync(STATE_FILE, { force: true });
  throw new Error(
    `API did not become ready (${exited !== null ? `exited with ${exited}` : 'timed out'}).
--- api.log ---
${output.slice(-6000)}`
  );
}

/** Registers the first account (which becomes Admin), confirms it from its .eml and saves the session. */
async function registerAdmin(state: E2eState): Promise<NonNullable<E2eState['admin']>> {
  const admin = newUserData('admin');
  await registerUser(state.baseURL, admin);
  const link = await waitForLink(state.pickupDir, admin.email, '/confirm-email');
  if (!link.startsWith(`${state.baseURL}/confirm-email?`)) throw new Error(`Unexpected confirmation link ${link}`);
  await confirmFromLink(state.baseURL, link);

  const api = await newApiContext(state.baseURL);
  try {
    const res = await api.post('/api/v1/auth/login', { data: { email: admin.email, password: admin.password, rememberMe: true } });
    if (res.status() !== 200) throw new Error(`Admin login failed: ${res.status()} ${await res.text()}`);
    const me = (await res.json()) as { roles: string[] };
    if (!me.roles.includes('Admin')) throw new Error(`First account is not Admin: ${JSON.stringify(me)}`);
    await api.storageState({ path: STORAGE_STATE });
  } finally {
    await api.dispose();
  }
  console.log(`[e2e] Registered the first account ${admin.email} (Admin).`);
  return admin;
}

/**
 * Exports the ASP.NET Core HTTPS development certificate (`dotnet dev-certs https`) to a
 * password-protected .pfx in the temp data dir for Kestrel. The browsers in this suite ignore
 * HTTPS errors, so the certificate only has to exist, not be trusted.
 */
function exportDevCertificate(dataDir: string): { path: string; password: string } {
  const check = spawnSync('dotnet', ['dev-certs', 'https', '--check'], { encoding: 'utf8' });
  if (check.status !== 0) {
    throw new Error(
      'No ASP.NET Core HTTPS development certificate found. Run `dotnet dev-certs https` once (optionally with --trust), then re-run the e2e suite.\n' +
        `${check.stdout}${check.stderr}`
    );
  }
  const pfx = path.join(dataDir, 'devcert.pfx');
  const password = crypto.randomBytes(18).toString('base64url');
  const exported = spawnSync('dotnet', ['dev-certs', 'https', '-ep', pfx, '-p', password], { encoding: 'utf8' });
  if (exported.status !== 0 || !fs.existsSync(pfx)) {
    throw new Error(`Exporting the development certificate failed:\n${exported.stdout}${exported.stderr}`);
  }
  return { path: pfx, password };
}

/** GET over HTTPS without certificate validation (the dev certificate may be untrusted). */
function httpsStatus(url: string): Promise<number> {
  return new Promise((resolve, reject) => {
    const req = https.get(url, { rejectUnauthorized: false, timeout: 5_000 }, (res) => {
      res.resume();
      resolve(res.statusCode ?? 0);
    });
    req.on('timeout', () => req.destroy(new Error('timeout')));
    req.on('error', reject);
  });
}

function freePort(): Promise<number> {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.unref();
    server.on('error', reject);
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address() as net.AddressInfo;
      server.close(() => resolve(port));
    });
  });
}

/** Pre-populates <dataDir>/pg_embed/binaries so PgServer skips the network download. */
function seedPostgresBinaries(dataDir: string): void {
  const source = [BINARIES_CACHE, INTEGRATION_BINARIES].find((dir) => fs.existsSync(dir) && fs.readdirSync(dir).length > 0);
  if (!source) return;
  const target = path.join(dataDir, 'pg_embed', 'binaries');
  fs.mkdirSync(target, { recursive: true });
  for (const file of fs.readdirSync(source)) fs.copyFileSync(path.join(source, file), path.join(target, file));
}

function cacheBinaries(dataDir: string): void {
  const downloaded = path.join(dataDir, 'pg_embed', 'binaries');
  if (fs.existsSync(BINARIES_CACHE) || !fs.existsSync(downloaded)) return;
  fs.mkdirSync(BINARIES_CACHE, { recursive: true });
  for (const file of fs.readdirSync(downloaded)) fs.copyFileSync(path.join(downloaded, file), path.join(BINARIES_CACHE, file));
}
