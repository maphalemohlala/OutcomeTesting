import { test as base, expect, type Page } from '@playwright/test';

/**
 * Shared ground for the portal end-to-end specs.
 *
 * NOTHING about the environment is written down here. The portal URL, the case ids and the
 * signed-in account all arrive as environment variables, because this file is committed and
 * they are environment detail - the same rule the rest of the repository follows for tenant
 * ids, org URLs and addresses. A checkout therefore carries no way to reach anybody's portal.
 *
 * Specs that need something unconfigured SKIP with the name of the missing variable rather
 * than failing. A red suite should mean the product is wrong, not that a laptop is unset;
 * a suite that cannot tell those apart gets ignored, and then it protects nothing.
 */

/** An environment variable, or undefined. Blank and whitespace count as unset. */
export function env(name: string): string | undefined {
  const raw = process.env[name];
  const value = (raw ?? '').trim();
  return value === '' ? undefined : value;
}

/**
 * Skips every test in the enclosing group unless all of `names` are set, naming the ones
 * that are not.
 *
 * Called at GROUP level rather than inside a test on purpose. A skip in the test body runs
 * after the `page` fixture has been built, so an unconfigured checkout still launches a
 * browser - and reports "Executable doesn't exist" rather than "OT_PORTAL_URL is not set",
 * which is a suite blaming the product for a laptop. Found by running it that way.
 */
export function requires(...names: readonly string[]): void {
  const missing = names.filter((name) => env(name) === undefined);
  base.skip(
    missing.length > 0,
    `not configured: ${missing.join(', ')} - see app/e2e/README.md`,
  );
}

/** The value of a variable `requires` has already guaranteed. */
export function requireEnv(name: string): string {
  const value = env(name);
  if (value === undefined) {
    throw new Error(`${name} is not set and its group did not declare it with requires()`);
  }

  return value;
}

export const PORTAL_URL = 'OT_PORTAL_URL';

/**
 * A case whose adviser maps to the signed-in user, so they ARE its T&C Manager.
 * The sign-off and regrade controls belong to them on this one.
 */
export const CASE_MAPPED_TO_ME = 'OT_CASE_MAPPED_TO_ME';

/**
 * A case with remedial actions awaiting sign-off whose adviser maps to SOMEBODY ELSE.
 * Both supervisor controls must be withheld here however the account is roled.
 *
 * The distinction matters and is easy to get wrong when picking fixtures: a case with
 * nothing pending renders neither the panel nor the refusal, so it cannot tell a working
 * gate from a broken one. Pick one that is genuinely waiting.
 */
export const CASE_MAPPED_ELSEWHERE = 'OT_CASE_MAPPED_ELSEWHERE';

/** The remediation page for one case. */
export function remediationUrl(portal: string, caseId: string): string {
  return `${portal.replace(/\/+$/, '')}/remediation/?case=${encodeURIComponent(caseId)}`;
}

/**
 * Liquid does not fail a page - it prints the exception into the markup and carries on, so a
 * broken fetch looks like a page that merely has nothing to show. Every spec asserts this,
 * because it is the failure mode that would otherwise pass every other assertion here.
 */
export async function expectNoLiquidError(page: Page): Promise<void> {
  await expect(page.locator('body')).not.toContainText('Liquid error');
}

/**
 * Fails loudly when the saved session has expired, instead of letting every spec report
 * "the control is not visible" - which is true of a sign-in page and means nothing.
 *
 * Both checks are POSITIVE, and that is the whole point. The first version asserted the body
 * did not say "Sign in", and a signed-out run then PASSED the two specs whose assertions are
 * `toHaveCount(0)`: no session, no controls, no controls expected, green. A suite that reports
 * a gate as working because nobody could see the page is worse than no suite. Anything phrased
 * as an absence has to be anchored to something whose presence was proved first.
 */
export async function expectSignedIn(page: Page, portal: string): Promise<void> {
  // Entra sends an expired session to a different host entirely, so the URL settles it
  // before any markup is read.
  expect(
    new URL(page.url()).host,
    'redirected off the portal - the session has expired, re-run `npm run e2e:auth`',
  ).toBe(new URL(portal).host);

  // And the signed-in chrome, which an anonymous page does not render.
  await expect(
    page.getByRole('navigation', { name: 'Main Navigation' }),
    'no main navigation - the page rendered anonymously',
  ).toBeVisible();
}

export const test = base;
export { expect };
