import * as fs from 'node:fs';

import { E2eState, STATE_FILE } from './support/env';
import { killTree, removeDirWithRetry, stopEmbeddedPostgres } from './support/processes';

/** Kills the API process tree, stops its private Postgres and deletes the temp data dir. */
export default async function globalTeardown(): Promise<void> {
  if (!fs.existsSync(STATE_FILE)) return;
  const state = JSON.parse(fs.readFileSync(STATE_FILE, 'utf8')) as E2eState;

  if (process.env['E2E_KEEP_LOG'] === '1' && fs.existsSync(state.logFile)) {
    console.log(`[e2e] --- api.log ---\n${fs.readFileSync(state.logFile, 'utf8').slice(-8000)}`);
  }

  killTree(state.pid);
  stopEmbeddedPostgres(state.dataDir);
  await removeDirWithRetry(state.dataDir);
  fs.rmSync(STATE_FILE, { force: true });
  if (fs.existsSync(state.dataDir)) console.warn(`[e2e] Could not fully delete ${state.dataDir}`);
}
