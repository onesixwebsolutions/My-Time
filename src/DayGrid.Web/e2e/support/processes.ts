import { execFileSync, spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';

/** Recursively finds the first file with the given name under `dir`. */
export function findFile(dir: string, name: string): string | null {
  if (!fs.existsSync(dir)) return null;
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isFile() && entry.name.toLowerCase() === name.toLowerCase()) return full;
    if (entry.isDirectory()) {
      const found = findFile(full, name);
      if (found) return found;
    }
  }
  return null;
}

/** Kills a process and all of its children. */
export function killTree(pid: number): void {
  if (process.platform === 'win32') {
    spawnSync('taskkill', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  } else {
    try {
      process.kill(-pid, 'SIGKILL');
    } catch {
      try {
        process.kill(pid, 'SIGKILL');
      } catch {
        /* already gone */
      }
    }
  }
}

/**
 * Stops the embedded Postgres that belongs to `dataDir` — and only that one. pg_ctl detaches
 * postgres.exe from the API's process tree, so killing the API alone would orphan it.
 * First tries a clean `pg_ctl stop`, then kills any postgres process whose command line
 * still references this (unique, temp) data directory. The system PostgreSQL service never
 * matches that filter.
 */
export function stopEmbeddedPostgres(dataDir: string): void {
  const pgCtl = findFile(dataDir, process.platform === 'win32' ? 'pg_ctl.exe' : 'pg_ctl');
  const pgData = findPgData(dataDir);
  if (pgCtl && pgData) {
    spawnSync(pgCtl, ['stop', '-D', pgData, '-m', 'fast', '-w', '-t', '30'], { stdio: 'ignore', timeout: 40_000 });
  }

  if (process.platform === 'win32') {
    const needle = path.resolve(dataDir).replace(/'/g, "''");
    const script =
      `Get-CimInstance Win32_Process -Filter "Name='postgres.exe'" | ` +
      `Where-Object { $_.CommandLine -and $_.CommandLine.Contains('${needle}') } | ` +
      `ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }`;
    try {
      execFileSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { stdio: 'ignore', timeout: 60_000 });
    } catch {
      /* best effort */
    }
  }
}

function findPgData(dataDir: string): string | null {
  const embedRoot = path.join(dataDir, 'pg_embed');
  if (!fs.existsSync(embedRoot)) return null;
  for (const entry of fs.readdirSync(embedRoot, { withFileTypes: true })) {
    const candidate = path.join(embedRoot, entry.name, 'data');
    if (entry.isDirectory() && fs.existsSync(path.join(candidate, 'PG_VERSION'))) return candidate;
  }
  return null;
}

/** Deletes a directory, retrying while Windows still holds file handles. */
export async function removeDirWithRetry(dir: string): Promise<void> {
  for (let attempt = 0; attempt < 40 && fs.existsSync(dir); attempt++) {
    try {
      fs.rmSync(dir, { recursive: true, force: true });
    } catch {
      await new Promise((r) => setTimeout(r, 250));
    }
  }
}
