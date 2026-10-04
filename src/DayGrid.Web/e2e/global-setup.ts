import { spawn, spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as net from 'node:net';
import * as os from 'node:os';
import * as path from 'node:path';

import {
  API_PROJECT,
  BINARIES_CACHE,
  E2eState,
  INTEGRATION_BINARIES,
  PUBLISH_DIR,
  REPO_ROOT,
  STATE_FILE
} from './support/env';
import { killTree, removeDirWithRetry, stopEmbeddedPostgres } from './support/processes';

const READY_TIMEOUT_MS = 240_000;

/**
 * 1. `dotnet publish` the API (its MSBuild targets run the Angular production build and copy
 *    it into wwwroot) — skipped with E2E_SKIP_PUBLISH=1 when a previous publish exists.
 * 2. Start the published API on a free loopback port with Database:Mode=Embedded and a fresh
 *    temp data directory (a private PostgreSQL 16 on a free port >= 5500; never 5432 and never
 *    %LocalAppData%\DayGrid).
 * 3. Wait for /health/ready.
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
      fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));
      process.env['E2E_BASE_URL'] = state.baseURL;
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
  const baseURL = `http://127.0.0.1:${port}`;
  const logFile = path.join(dataDir, 'api.log');
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
      Database__Mode: 'Embedded',
      Database__EmbeddedDataDir: dataDir,
      App__TimeZone: 'Asia/Kolkata',
      // Never let a reminder reach a real SMTP server during a test run.
      Email__Host: '127.0.0.1',
      Email__Port: '9',
      Email__Password: ''
    }
  });
  fs.closeSync(log);

  const state: E2eState = { pid: child.pid!, dataDir, logFile, baseURL };
  // Written before waiting so globalTeardown can still clean up if setup is interrupted.
  fs.writeFileSync(STATE_FILE, JSON.stringify(state, null, 2));

  let exited: number | null = null;
  child.on('exit', (code) => (exited = code ?? -1));

  console.log(`[e2e] Starting API at ${baseURL} (data dir ${dataDir}) …`);
  const deadline = Date.now() + READY_TIMEOUT_MS;
  while (Date.now() < deadline) {
    if (exited !== null) break;
    try {
      const res = await fetch(`${baseURL}/health/ready`);
      if (res.ok) {
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
