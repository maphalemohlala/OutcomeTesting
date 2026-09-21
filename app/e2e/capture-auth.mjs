/**
 * Saves a signed-in portal session for the e2e specs to reuse.
 *
 * Power Pages authenticates through Entra ID, so the sign-in cannot be scripted from a
 * password - and it should not be: nobody should be typing one into a repository. This
 * opens a real browser, waits for the sign-in to be completed BY HAND, and then stores the
 * cookies.
 *
 * The file it writes is a live session. It is gitignored and it expires; when the specs
 * start reporting "looks signed out", run this again.
 *
 *   OT_PORTAL_URL=<portal> node e2e/capture-auth.mjs
 *
 * Pass --profile <dir> to reuse a browser profile that is ALREADY signed in, in which case
 * it takes the session without asking anybody to type anything.
 */
import { chromium } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';

const OUT = resolve(process.cwd(), 'e2e/.auth/portal.json');
const portal = (process.env.OT_PORTAL_URL ?? '').trim();

if (portal === '') {
  console.error('OT_PORTAL_URL is not set. See app/e2e/README.md.');
  process.exit(2);
}

const profileArg = process.argv.indexOf('--profile');
const profile = profileArg === -1 ? undefined : process.argv[profileArg + 1];

mkdirSync(dirname(OUT), { recursive: true });

const context = profile
  ? await chromium.launchPersistentContext(profile, { headless: false })
  : await (await chromium.launch({ headless: false })).newContext();

const page = context.pages()[0] ?? (await context.newPage());
await page.goto(portal);

const signedIn = async () => {
  const body = await page.locator('body').innerText().catch(() => '');
  return body.includes('My Work') || body.includes('Sign out') || body.includes('Remediation');
};

if (!(await signedIn())) {
  console.log('Sign in to the portal in the window that opened. Waiting up to 5 minutes…');
  const until = Date.now() + 5 * 60_000;
  while (Date.now() < until && !(await signedIn())) {
    await page.waitForTimeout(2_000);
  }
}

if (!(await signedIn())) {
  console.error('Still not signed in. Nothing was saved.');
  await context.close();
  process.exit(1);
}

await context.storageState({ path: OUT });
console.log(`Saved the portal session to ${OUT}`);
await context.close();
