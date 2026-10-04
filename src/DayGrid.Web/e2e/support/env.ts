import * as os from 'node:os';
import * as path from 'node:path';

// Shared paths for the e2e harness. Everything lives under the OS temp folder — never under
// %LocalAppData%\DayGrid (the owner's real embedded database) and never against the system
// PostgreSQL service on 5432: the API runs with Database:Mode=Embedded pointed at a fresh,
// throwaway data directory that globalTeardown deletes.

export const REPO_ROOT = path.resolve(__dirname, '..', '..', '..', '..');
export const API_PROJECT = path.join(REPO_ROOT, 'src', 'DayGrid.Api', 'DayGrid.Api.csproj');

/** Reused between runs so E2E_SKIP_PUBLISH=1 can skip the ~1 min publish. */
export const PUBLISH_DIR = process.env['E2E_PUBLISH_DIR'] ?? path.join(os.tmpdir(), 'daygrid-e2e-publish');

/** Downloaded Postgres binaries are cached here so each run doesn't refetch ~23 MB. */
export const BINARIES_CACHE = path.join(os.tmpdir(), 'daygrid-e2e-cache', 'binaries');
/** The .NET integration tests keep the same binaries here — reused if present. */
export const INTEGRATION_BINARIES = path.join(os.tmpdir(), 'daygrid-integration-tests', 'pg_embed', 'binaries');

/** globalSetup -> globalTeardown hand-off (pid, temp dir). */
export const STATE_FILE = path.join(os.tmpdir(), 'daygrid-e2e-state.json');

export interface E2eState {
  pid: number;
  dataDir: string;
  logFile: string;
  baseURL: string;
}
