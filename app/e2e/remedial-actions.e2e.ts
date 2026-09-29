import type { Page } from '@playwright/test';
import {
  PORTAL_URL,
  expect,
  expectNoLiquidError,
  expectSignedIn,
  remediationUrl,
  requireEnv,
  requires,
  test,
} from './portal';

/**
 * The checker writes the remedial actions; the adviser says whether each was performed
 * (project owner, 2026-09-29, AD-225), against a live portal.
 *
 * The vitest specs pin the template source and the plug-in tests pin the server rules. What
 * only a browser can say is whether the served page draws the card from the answers it
 * actually has, refuses a submit before sending anything, and renders the nine columns from
 * rows Dataverse actually returns.
 *
 * Nothing here writes. The review-page tests abort every non-GET Web API call and fail if the
 * page attempted one, so a refused submit is proved to have been refused BEFORE a round trip;
 * the write-once test deliberately attempts a write it expects to be refused.
 */

/** An editable review, assigned to the signed-in user, that owes a remediation with at least one row still blank. */
const REMEDIAL_REVIEW_URL = 'OT_REMEDIAL_REVIEW_URL';
/** A submitted review. */
const SUBMITTED_REVIEW_URL = 'OT_SUBMITTED_REVIEW_URL';
/** A case whose remedial actions carry the checker's words and an Action performed answer (both Yes and No). */
const REMEDIAL_CASE = 'OT_REMEDIAL_CASE';
/** One of that case's actions. */
const REMEDIAL_ACTION_ID = 'OT_REMEDIAL_ACTION_ID';

const COLUMNS = [
  'No.',
  'Issue / fail reason',
  'Remedial action',
  'Action performed',
  'Owner',
  'Target date',
  'Status',
  'Age',
  'Sign-off',
];

/** Aborts every write the page sends to the portal Web API and returns the list it tried. */
async function refuseWrites(page: Page): Promise<string[]> {
  const attempted: string[] = [];
  await page.route('**/_api/**', async (route) => {
    const request = route.request();
    if (request.method() === 'GET') {
      await route.continue();
      return;
    }

    attempted.push(`${request.method()} ${request.url()}`);
    await route.abort();
  });
  return attempted;
}

/** The card's rows as { label, text }, read from the served DOM. */
async function cardRows(page: Page): Promise<{ label: string; text: string }[]> {
  return page.locator('[data-ot-remedial-rows] tr').evaluateAll((rows) =>
    rows.map((row) => {
      const cells = row.querySelectorAll('td');
      const box = row.querySelector('textarea');
      return {
        label: (cells[1]?.textContent ?? '').trim(),
        text: box ? box.value : '',
      };
    }),
  );
}

test.describe('the fail points and remedial actions card on a review', () => {
  requires(PORTAL_URL, REMEDIAL_REVIEW_URL);

  test('draws one required box per fail point, from the answers on the page', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const attempted = await refuseWrites(page);

    await page.goto(requireEnv(REMEDIAL_REVIEW_URL));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    const card = page.locator('[data-ot-remedial]');
    await expect(card, 'the review page has no remedial-actions card').toHaveCount(1);
    await expect(card, 'the card is hidden - the fixture review must owe a remediation').toBeVisible();
    await expect(card.getByRole('heading', { name: 'Fail points and remedial actions' })).toBeVisible();

    const rows = await cardRows(page);
    expect(rows.length, 'the card is visible but lists nothing').toBeGreaterThan(0);
    for (const row of rows) {
      expect(row.label, 'a row with no fail point label').not.toBe('');
    }

    // One box per row, and every box live on an open review.
    const boxes = card.locator('[data-ot-remedial-rows] textarea');
    await expect(boxes).toHaveCount(rows.length);
    for (let i = 0; i < rows.length; i++) {
      await expect(boxes.nth(i)).toBeEditable();
    }

    expect(attempted, 'opening the page wrote something').toEqual([]);
  });

  test('refuses Submit while a row is blank, naming it, before sending anything', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const attempted = await refuseWrites(page);

    await page.goto(requireEnv(REMEDIAL_REVIEW_URL));
    await expectSignedIn(page, portal);
    await expect(page.locator('[data-ot-remedial]')).toBeVisible();

    // The precondition, proved rather than assumed: a fixture whose rows are all written
    // would SUBMIT the review on the click below (the write guard would abort it, but the
    // test would then be about the guard, not the refusal).
    const rows = await cardRows(page);
    const blank = rows.find((row) => row.text.trim() === '');
    expect(blank, 'every row already has words - pick a review with a blank row').toBeDefined();

    const expected =
      blank!.label === 'Overall'
        ? 'Write the overall remedial action under "Fail points and remedial actions" before submitting.'
        : `Write the remedial action for "${blank!.label}" before submitting.`;

    const submit = page.locator('[data-ot-submit-button]');
    await expect(submit).toBeEnabled();
    await submit.click();

    await expect(page.locator('[data-ot-submit-status]')).toContainText(expected);

    // Still open: the button is live and was not relabelled, and the review is not marked
    // Submitted in the summary.
    await expect(submit).toBeEnabled();
    await expect(submit).not.toHaveText(/Submitted/);
    await expect(page.locator('[data-ot-review-status]')).not.toContainText('Submitted');

    expect(attempted, 'the refused submit still sent a write').toEqual([]);
  });
});

test.describe('a submitted review', () => {
  requires(PORTAL_URL, SUBMITTED_REVIEW_URL);

  test('carries no remedial-actions card and no Submit', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);

    await page.goto(requireEnv(SUBMITTED_REVIEW_URL));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // Anchored first: the page IS a submitted review, so the absences below mean something.
    await expect(page.locator('[data-ot-submitted-on]').first()).not.toHaveText('—');

    await expect(page.locator('[data-ot-remedial]')).toHaveCount(0);
    await expect(page.locator('[data-ot-submit-button]')).toHaveCount(0);
  });
});

/** The table carrying the remedial actions, its header texts, and each full row's cell texts. */
async function actionsTable(page: Page): Promise<{ heads: string[]; rows: string[][] }> {
  const table = page.locator('table', { has: page.locator('th', { hasText: 'Action performed' }) }).first();
  await expect(table, 'no table with an "Action performed" column').toBeVisible();

  const heads = (await table.locator('thead th').allInnerTexts()).map((h) => h.trim());
  const rows = await table.locator('tbody tr').evaluateAll((trs) =>
    trs.map((tr) => [...tr.children].map((td) => (td.textContent ?? '').replace(/\s+/g, ' ').trim())),
  );

  // A group heading is one cell spanning the table, and the later rows of a rowspan group
  // carry only their own cells; only the first row of each action has every column.
  return { heads, rows: rows.filter((cells) => cells.length === heads.length) };
}

async function expectRemedialColumns(page: Page): Promise<void> {
  const { heads, rows } = await actionsTable(page);

  expect(heads, 'the nine columns, Action performed between Remedial action and Owner').toEqual(COLUMNS);
  expect(rows.length, 'the case shows no actions - check OT_REMEDIAL_CASE').toBeGreaterThan(0);

  const remedial = COLUMNS.indexOf('Remedial action');
  const performed = COLUMNS.indexOf('Action performed');

  for (const cells of rows) {
    // The checker's words, not a blank or a placeholder.
    expect(cells[remedial], `issue ${cells[0]} shows no remedial action`).not.toMatch(/^(—)?$/);
    // The adviser's answer, with any note after it.
    expect(cells[performed], `issue ${cells[0]} shows no Action performed answer`).toMatch(/^(Yes|No|—)/);
  }

  // Both answers, which is what the fixture case must carry: a page that printed "Yes" on
  // every row regardless would pass the loop above.
  // The answer is followed directly by the note (a <br />, so no space in textContent).
  const answers = rows.map((cells) => /^(Yes|No|—)/.exec(cells[performed])?.[1]);
  expect(answers).toContain('Yes');
  expect(answers).toContain('No');
}

test.describe('the Action performed column', () => {
  requires(PORTAL_URL, REMEDIAL_CASE);

  test('on OT Remediation, between Remedial action and Owner, read-only once completed', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);

    await page.goto(remediationUrl(portal, requireEnv(REMEDIAL_CASE)));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    await expectRemedialColumns(page);

    // Completed actions are frozen: no live Yes / No and no editable note, whoever is signed in.
    await expect(page.locator('[data-ot-performed-choice]:enabled')).toHaveCount(0);
    await expect(page.locator('[data-ot-performed-note]:not([readonly]):not(:disabled)')).toHaveCount(0);
  });

  test('on the case record, the same nine columns', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);

    await page.goto(
      `${portal.replace(/\/+$/, '')}/case-details?id=${encodeURIComponent(requireEnv(REMEDIAL_CASE))}`,
    );
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    await expectRemedialColumns(page);
    await expect(page.locator('[data-ot-performed-choice]')).toHaveCount(0);
  });
});

test.describe("the checker's remedial action", () => {
  requires(PORTAL_URL, REMEDIAL_ACTION_ID);

  test('cannot be rewritten from the browser', async ({ page, request }) => {
    const portal = requireEnv(PORTAL_URL).replace(/\/+$/, '');
    const actionId = requireEnv(REMEDIAL_ACTION_ID);

    await page.goto(portal);
    await expectSignedIn(page, portal);

    // The anti-forgery token, so the refusal below can only be about the column.
    const tokenPage = await request.get(`${portal}/_layout/tokenhtml`);
    expect(tokenPage.status(), 'could not reach the anti-forgery token endpoint').toBe(200);
    const token = (await tokenPage.text()).match(
      /name="__RequestVerificationToken"[^>]*value="([^"]+)"/,
    )?.[1];
    expect(token, 'no __RequestVerificationToken in /_layout/tokenhtml').toBeTruthy();

    // Readable first, so the session, the id and the Web API are proved live.
    const read = await request.get(
      `${portal}/_api/al_remediationactions(${actionId})?$select=al_actionperformed`,
      { failOnStatusCode: false },
    );
    expect(read.status(), 'the action is not readable - check OT_REMEDIAL_ACTION_ID').toBe(200);

    const refused = await request.patch(`${portal}/_api/al_remediationactions(${actionId})`, {
      headers: {
        'Content-Type': 'application/json',
        __RequestVerificationToken: token as string,
      },
      data: { al_remedialaction: 'Rewritten from the browser by the e2e spec' },
      failOnStatusCode: false,
    });

    expect(refused.status(), "the checker's remedial action is writable from the browser").toBeGreaterThanOrEqual(400);
    const body = (await refused.text()).toLowerCase();
    expect(body).not.toContain('anti-forgery');
    expect(body).not.toContain('requestverificationtoken');
  });
});
