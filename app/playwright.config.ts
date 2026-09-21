import { defineConfig, devices } from '@playwright/test';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

/**
 * End-to-end specs against a LIVE Power Pages portal.
 *
 * Separate from vitest on purpose. Vitest reads the template source and pins the Liquid;
 * these drive a browser and answer the questions the source cannot - whether the table
 * permission exists, whether contact scope resolves, whether a fetch returns anything.
 *
 * The two never collide because these files are `*.e2e.ts` and vitest only collects
 * `*.test.*` and `*.spec.*`.
 */

// package.json has no "type", but Playwright loads a .ts config as an ES module, where
// __dirname does not exist. Derived from import.meta instead so the path is right whichever
// directory the run starts in.
const HERE = dirname(fileURLToPath(import.meta.url));
const AUTH = resolve(HERE, 'e2e/.auth/portal.json');

export default defineConfig({
  testDir: './e2e',
  testMatch: /.*\.e2e\.ts/,

  // One at a time. These read a shared environment whose data other people are changing
  // during UAT, and a parallel run would report races as failures.
  workers: 1,
  fullyParallel: false,

  // No retries. A flaky pass against a live environment is worse than a failure: it hides
  // the thing that was intermittent, which on this portal has twice been the real defect.
  retries: 0,

  reporter: [['list']],

  use: {
    ...devices['Desktop Chrome'],
    // Missing on a fresh checkout, which is correct - the specs skip rather than fail, and
    // `npm run e2e:auth` writes it. It is gitignored: it is a live session.
    storageState: existsSync(AUTH) ? AUTH : undefined,
    baseURL: process.env.OT_PORTAL_URL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  // A portal page is server-rendered and slow to first byte; the default 30s is tight when
  // the site has just been restarted.
  timeout: 60_000,
  expect: { timeout: 15_000 },
});
