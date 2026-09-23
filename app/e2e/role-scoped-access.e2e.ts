import { type Page } from '@playwright/test';
import {
  PORTAL_URL,
  env,
  expect,
  expectNoLiquidError,
  expectSignedIn,
  requireEnv,
  requires,
  test,
} from './portal';

/**
 * Role-scoped portal access (AD-218, AR-02 to AR-04), against a live portal.
 *
 * The vitest suite pins the table permission YAML and the Liquid source. It cannot see
 * whether a Contact- or Account-scoped permission actually resolves for a signed-in contact,
 * which is the whole of AD-218. Every group here is therefore run AS the role it is about,
 * with that role's own saved session, and every "cannot see" assertion is anchored to a
 * proved sign-in and to a case the same session CAN see - an empty page proves nothing.
 *
 * Nothing about the environment is written here. Each group names the session file and the
 * case ids it needs, and skips with their names when they are not set. The account behind a
 * session must hold ONLY the role its group is about: a second role (above all Outcome
 * Testing Manager or Administrators, which read every case) makes the refusals pass for the
 * wrong reason or fail for the right one.
 *
 * Read-only. No group writes anything.
 */

/** Session files, one per role, each captured with `npm run e2e:auth` and renamed. */
const SESSION_ADVISER = 'OT_SESSION_ADVISER';
const SESSION_TAX_REVIEWER = 'OT_SESSION_TAX_REVIEWER';
const SESSION_AQS_REVIEWER = 'OT_SESSION_AQS_REVIEWER';
const SESSION_OVERSIGHT = 'OT_SESSION_OVERSIGHT';

/** A case released to the adviser session (at or past Remedial Action Required). */
const CASE_RELEASED_TO_ME = 'OT_CASE_RELEASED_TO_ME';
/** A case whose adviser is the adviser session but which is still in review. */
const CASE_MINE_IN_REVIEW = 'OT_CASE_MINE_IN_REVIEW';
/** A case that belongs to none of the restricted sessions: another adviser, another checker. */
const CASE_NOT_MINE = 'OT_CASE_NOT_MINE';
/** A case waiting in the AQS queue, and its reference as the queue lists it. */
const CASE_IN_AQS_QUEUE = 'OT_CASE_IN_AQS_QUEUE';
const CASE_IN_AQS_QUEUE_REF = 'OT_CASE_IN_AQS_QUEUE_REF';

async function openCase(page: Page, portal: string, caseId: string): Promise<void> {
  await page.goto(`${portal.replace(/\/+$/, '')}/case-details?id=${encodeURIComponent(caseId)}`);
  await expectSignedIn(page, portal);
  await expectNoLiquidError(page);
}

async function expectCaseShown(page: Page): Promise<void> {
  await expect(page.getByText('Case not available', { exact: true })).toHaveCount(0);
  await expect(page.locator('body')).toContainText('Review progress');
}

async function expectCaseWithheld(page: Page): Promise<void> {
  await expect(page.getByText('Case not available', { exact: true })).toBeVisible();
  await expect(page.locator('body')).not.toContainText('Review progress');
}

/** The Web API refuses the row outright, whatever the page chose to render. */
async function expectApiRefuses(page: Page, portal: string, caseId: string): Promise<void> {
  const response = await page.request.get(
    `${portal.replace(/\/+$/, '')}/_api/al_outcomecases(${caseId})?$select=al_casestatus`,
    { headers: { Accept: 'application/json' } },
  );
  expect(response.status(), 'the portal Web API returned a case this role may not read').toBe(403);
}

async function expectPageDenied(page: Page, portal: string, path: string): Promise<void> {
  const response = await page.goto(`${portal.replace(/\/+$/, '')}${path}`);
  expect(response?.status(), `${path} should be denied to this role`).toBe(403);
  await expect(page.getByRole('heading', { name: 'Access Denied' })).toBeVisible();
}

test.describe('an adviser (AR-04)', () => {
  requires(PORTAL_URL, SESSION_ADVISER, CASE_RELEASED_TO_ME, CASE_MINE_IN_REVIEW, CASE_NOT_MINE);
  test.use({ storageState: env(SESSION_ADVISER) });

  test('sees their own case once it is released for remediation', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_RELEASED_TO_ME));
    await expectCaseShown(page);
  });

  test('does not see their own case while it is still in review', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_MINE_IN_REVIEW));
    await expectCaseWithheld(page);
    await expectApiRefuses(page, portal, requireEnv(CASE_MINE_IN_REVIEW));
  });

  test("does not see another adviser's case", async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_NOT_MINE));
    await expectCaseWithheld(page);
    await expectApiRefuses(page, portal, requireEnv(CASE_NOT_MINE));
  });

  test('is not given the reviewer pages', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await expectPageDenied(page, portal, '/tax-reviews');
    await expectPageDenied(page, portal, '/aqs-reviews');
  });
});

test.describe('a Tax reviewer (AR-02)', () => {
  requires(PORTAL_URL, SESSION_TAX_REVIEWER, CASE_NOT_MINE, CASE_IN_AQS_QUEUE);
  test.use({ storageState: env(SESSION_TAX_REVIEWER) });

  test('has the Tax review page, scoped to their own reviews', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await page.goto(`${portal.replace(/\/+$/, '')}/tax-reviews`);
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);
    await expect(page.getByRole('heading', { name: 'Tax reviews', level: 1 })).toBeVisible();
    await expect(page.locator('body')).not.toContainText('All reviews');
  });

  test('does not see a case allocated to nobody or to someone else', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_NOT_MINE));
    await expectCaseWithheld(page);
    await expectApiRefuses(page, portal, requireEnv(CASE_NOT_MINE));
  });

  test('does not read the AQS queue', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_IN_AQS_QUEUE));
    await expectCaseWithheld(page);
  });
});

test.describe('an AQS reviewer (AR-03)', () => {
  requires(PORTAL_URL, SESSION_AQS_REVIEWER, CASE_IN_AQS_QUEUE, CASE_IN_AQS_QUEUE_REF, CASE_NOT_MINE);
  test.use({ storageState: env(SESSION_AQS_REVIEWER) });

  test('sees a waiting case in the queue, with its age in working days', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await page.goto(`${portal.replace(/\/+$/, '')}/aqs-reviews`);
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // The two queue groups, Tax-reviewed first (AR-03).
    const body = page.locator('body');
    await expect(body).toContainText('Tax review completed – awaiting allocation');
    await expect(body).toContainText('New – awaiting allocation');

    const row = page.getByRole('row').filter({ hasText: requireEnv(CASE_IN_AQS_QUEUE_REF) });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText('working day');
  });

  test('can open the waiting case', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_IN_AQS_QUEUE));
    await expectCaseShown(page);
  });

  test("does not see another checker's case", async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await openCase(page, portal, requireEnv(CASE_NOT_MINE));
    await expectCaseWithheld(page);
    await expectApiRefuses(page, portal, requireEnv(CASE_NOT_MINE));
  });
});

test.describe('an oversight role (Outcome Testing Manager or Administrators)', () => {
  requires(PORTAL_URL, SESSION_OVERSIGHT, CASE_NOT_MINE, CASE_MINE_IN_REVIEW, CASE_IN_AQS_QUEUE);
  test.use({ storageState: env(SESSION_OVERSIGHT) });

  test('still reads every case, including the ones the other roles are refused', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    for (const name of [CASE_NOT_MINE, CASE_MINE_IN_REVIEW, CASE_IN_AQS_QUEUE]) {
      await openCase(page, portal, requireEnv(name));
      await expectCaseShown(page);
    }
  });
});
