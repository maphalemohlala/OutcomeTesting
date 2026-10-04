import type { Frame } from '@playwright/test';
import { env, expect, expectNoLiquidError, expectSignedIn, test } from './portal';
import {
  ADVISER_NAME,
  CLIENT_NAME,
  PARAPLANNER_NAME,
  PRODUCT,
  RunLogger,
  SESSION_FILE,
  allocateCheck,
  answerReview,
  caseReference,
  expectNotUnavailable,
  extractCsv,
  findCaseId,
  go,
  lifecycleGate,
  openCase,
  openCodeApp,
  openEditableReview,
  pollPortal,
  portalBase,
  readPortalIdentity,
  remediationCaseUrl,
  remediationListUrl,
  reopen,
  sameEmail,
  sessionFilePresent,
  submitReview,
  summaryValue,
  testerEmail,
  waitCaseStatus,
  withSave,
  writeRemedialActions,
} from './lifecycle';

/**
 * ONE case, the whole lifecycle, in PROD - written for the owner to run after importing
 * 1.0.19.0 (owner, 2026-10-04: "full lifecycle, one test case").
 *
 *   upload (Code App) -> Queued -> allocate the Tax check -> answer and submit it on the
 *   portal -> allocate the AQS check -> answer it with a failing grade and submit ->
 *   the remediation reaches the adviser by EMAIL -> the adviser answers and completes it ->
 *   T&C sign-off -> regrade closes the case -> the Code App agrees.
 *
 * The tester is every person on the case: adviser, paraplanner, both checkers and the T&C
 * Manager, by their own mailbox (OT_E2E_TESTER_EMAIL), so every letter the run causes goes to
 * them. The case is named so nobody actions it and its reference starts 999.
 *
 * It WRITES to whatever it is pointed at, so it runs only when OT_E2E_WRITE=PROD-LIFECYCLE
 * and OT_E2E_TARGET names the portal host it is pointed at. Without both, every test skips
 * and nothing is touched - which is what the default `npm run e2e` sees.
 *
 * Serial: each step needs the one before, and a failure stops the run where it is. The run
 * log in e2e/.runs/<ref>.json says how far it got and what it created.
 *
 * See app/e2e/PROD-LIFECYCLE.md for the prerequisites and how to read a failure.
 */

const gate = lifecycleGate();
const ref = caseReference();
const log = gate === null ? new RunLogger(ref) : null;

/** What the run learns, step by step. Module state survives between the serial tests. */
const run: {
  testerName?: string;
  caseId?: string;
  taxReviewId?: string;
  aqsReviewId?: string;
} = {};

const NOTE = `E2E TEST ${ref} - automated lifecycle run, do not action.`;

/** The web roles the run needs on the portal, whatever the product is called. */
const REQUIRED_WEB_ROLES = [
  'AL Portal - Tax Reviewer',
  'AL Portal - AQS Reviewer',
  'AL Portal - Adviser Remediation',
  'AL Portal - T&C Supervisor',
];

/** Either of these allocates both checks (AllocationScope); so do both team-manager roles together. */
function allocatingRoles(): string[] {
  const products = new Set([env(PRODUCT) ?? 'OTIS', 'OTIS', 'Outcome Testing']);
  return [...[...products].map((product) => `AL Portal - ${product} Manager`), 'Administrators'];
}

function need<T>(value: T | undefined, what: string): T {
  if (value === undefined) { throw new Error(`${what} is not known - an earlier step did not record it`); }
  return value;
}

/** The status badge on a portal remediation case page. */
async function portalCaseStatus(page: import('@playwright/test').Page): Promise<string> {
  const badge = page.locator('[data-ot-case-status]').first();
  return (await badge.count()) > 0 ? (await badge.innerText()).trim() : '';
}

test.describe('PROD full lifecycle, one marked test case', () => {
  test.skip(gate !== null, gate ?? '');
  test.describe.configure({ mode: 'serial', timeout: 25 * 60_000 });
  // A control that is not there fails in a minute with its name, rather than holding the
  // step for its whole 25 minutes. The long waits are the polls, which set their own limits.
  test.use({ actionTimeout: 60_000, navigationTimeout: 120_000 });

  // eslint-disable-next-line no-empty-pattern
  test.afterEach(async ({}, testInfo) => {
    log?.step({
      title: testInfo.title,
      status: testInfo.status ?? 'unknown',
      at: new Date().toISOString(),
      durationMs: testInfo.duration,
      ...(testInfo.error?.message ? { error: testInfo.error.message.slice(0, 2000) } : {}),
    });
  });

  test('1. preflight: the session, the tester and every prerequisite are in place', async ({ page }) => {
    const email = testerEmail();
    log?.note(`run started against ${portalBase()} as ${email}; case reference ${ref}`);

    // The session file both halves share.
    expect(sessionFilePresent(), `no saved session at ${SESSION_FILE} - run \`npm run e2e:auth\` with OT_PORTAL_URL set to this portal`).toBe(true);

    // Portal: signed in, and as the tester.
    await page.goto(`${portalBase()}/`);
    await expectSignedIn(page, portalBase());
    const who = await readPortalIdentity(page);
    expect(
      sameEmail(who.email, email),
      `OT_E2E_TESTER_EMAIL is ${email} but the portal session is ${who.name} (${who.email}); the run only writes as the tester whose mailbox receives its letters`,
    ).toBe(true);
    run.testerName = who.name;
    log?.set({ testerName: who.name, portalRoles: who.roles ?? undefined });

    // Portal roles: reviewer for both checks, adviser remediation, T&C sign-off, and one that allocates both.
    expect(who.roles, `the portal page at ${who.source} did not list the session's roles, so they cannot be checked`).not.toBeNull();
    const held = new Set((who.roles ?? []).map((role) => role.toLowerCase()));
    const missingRoles = REQUIRED_WEB_ROLES.filter((role) => !held.has(role.toLowerCase()));
    const allocates = allocatingRoles().some((role) => held.has(role.toLowerCase()))
      || (held.has('al portal - tax team manager') && held.has('al portal - aqs team manager'));
    if (!allocates) { missingRoles.push(`one of ${allocatingRoles().join(' / ')} (allocates both checks)`); }
    expect(missingRoles, `the tester's portal session lacks: ${missingRoles.join('; ')}`).toEqual([]);

    // Portal: the remediation area opens. (The checker and sign-off areas need a case; the
    // roles above are what open them, and the steps below prove each on the test case.)
    const remediation = await page.goto(`${portalBase()}/remediation`);
    expect(remediation?.status(), 'the portal refused /remediation').toBeLessThan(400);
    await expectNoLiquidError(page);
    await expect(page.getByRole('search', { name: 'Search remediation by case reference' })).toBeVisible();

    // Code App: opens, can upload, can list cases, can allocate.
    const frame = await openCodeApp(page);
    await go(frame, '#/imports');
    await frame.locator('button.intake__btn, section.page-unavailable').first().waitFor({ timeout: 60_000 });
    await expectNotUnavailable(frame, 'Case intake');
    await expect(frame.getByRole('button', { name: 'Upload cases' }), 'Case intake offers Upload cases').toBeEnabled();

    await go(frame, '#/cases');
    await frame.locator('table.worklist, section.page-unavailable, .worklist__unavailable').first().waitFor({ timeout: 60_000 });
    await expectNotUnavailable(frame, 'Cases');
    await expect(frame.locator('table.worklist'), 'the case worklist lists cases').toBeVisible();

    const anyCase = frame.locator('table.worklist tbody th a').first();
    if ((await anyCase.count()) > 0) {
      // Read-only: the modal is opened and cancelled. Its Checkers section renders only for
      // an account holding the allocate permission.
      await anyCase.click();
      await frame.getByRole('button', { name: 'Edit case details' }).click();
      const dialog = frame.getByRole('dialog', { name: 'Edit case details' });
      await expect(dialog.locator('legend', { hasText: /^Checkers$/ }), 'the Code App offers allocation (command.assign)').toBeVisible();
      await dialog.getByRole('button', { name: 'Cancel' }).click();
    } else {
      log?.note('preflight: no case on the worklist to open, so allocation is first proved at step 3');
    }

    // Code App: the tester is one person in the directory, keyed by email.
    await go(frame, '#/admin/people');
    await frame.locator('#people-search, section.page-unavailable').first().waitFor({ timeout: 60_000 });
    await expectNotUnavailable(frame, 'People');
    await frame.locator('#people-search').fill(email);
    await expect.poll(async () => (await peopleRowsFor(frame, email)).length, {
      message: `People lists ${email} exactly once (an active contact holding the email)`, timeout: 60_000,
    }).toBe(1);

    // Code App: the T&C mapping that lets the tester sign their own case off.
    await expectMappedToSelf(frame, email, who.name);
  });

  test('2. upload one marked case through the Code App, and it is queued Tax then AQS', async ({ page }) => {
    const email = testerEmail();
    const csv = extractCsv(ref, email);

    const frame = await openCodeApp(page);
    await go(frame, '#/imports');
    await frame.getByRole('button', { name: 'Upload cases' }).waitFor({ timeout: 60_000 });
    await frame.locator('input[type="file"]').setInputFiles({
      name: `e2e-${ref}.csv`,
      mimeType: 'text/csv',
      buffer: Buffer.from(csv, 'utf8'),
    });

    const outcome = frame.locator('.intake__notice--success, .intake__notice--error').first();
    await outcome.waitFor({ timeout: 180_000 });
    const said = (await outcome.innerText()).replace(/\s+/g, ' ').trim();
    expect(await outcome.getAttribute('class'), `the upload failed: ${said}`).toContain('intake__notice--success');
    expect(said, 'one row, imported, none already existing or failed').toMatch(/: 1 of 1 case imported\./);
    const batch = /^Batch (\S+):/.exec(said)?.[1];
    log?.set({ importBatch: batch });

    run.caseId = await findCaseId(frame, ref);
    log?.set({ caseId: run.caseId });

    await openCase(frame, run.caseId);
    expect(await summaryValue(frame, 'Reference')).toBe(ref);
    expect(await summaryValue(frame, 'Route'), 'a stamped "Tax Check" item routes the case Tax then AQS').toBe('Tax then AQS');
    expect(await summaryValue(frame, 'Status'), 'the import queues a routed case').toBe('Queued');
  });

  test('3. allocate the Tax check to the tester in the Code App', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');
    const frame = await openCodeApp(page);
    const check = await allocateCheck(frame, caseId, 'Tax', testerEmail(), NOTE);
    run.taxReviewId = check.id;
    log?.set({ taxReviewId: check.id });
    log?.note(`Tax check ${check.reference} allocated; owner shown as "${check.owner}"`);
    expect(await summaryValue(frame, 'Status')).toMatch(/^(Assigned|Review In Progress)$/);
  });

  test('4. the Tax review header names each person, then their email, both the tester\'s', async ({ page }) => {
    const reviewId = need(run.taxReviewId, 'the Tax review id');
    await openEditableReview(page, reviewId, ref);

    const header = await page.evaluate(() => {
      const keys = ['al_advisername', 'al_adviseremail', 'al_paraplanner', 'al_paraplanneremail'];
      const found = keys.map((key) => document.querySelector<HTMLInputElement>(`[data-ot-hdr="${key}"]`));
      const inOrder = found.every((el, i) => {
        if (!el) { return false; }
        const previous = found[i - 1];
        return i === 0 || (!!previous && (previous.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0);
      });
      return {
        present: found.map((el) => !!el),
        inOrder,
        values: found.map((el) => el?.value ?? ''),
        labels: found.map((el) => (el?.closest('td')?.previousElementSibling?.textContent ?? '').trim()),
      };
    });

    expect(header.present, 'the editable header carries all four person fields').toEqual([true, true, true, true]);
    expect(header.labels).toEqual(['Adviser name', 'Adviser email', 'Paraplanner', 'Paraplanner email']);
    expect(header.inOrder, 'adviser name, adviser email, paraplanner, paraplanner email - each email after its own name').toBe(true);
    expect(header.values[0]).toBe(ADVISER_NAME);
    expect(header.values[2]).toBe(PARAPLANNER_NAME);
    expect(sameEmail(header.values[1], testerEmail()), `adviser email is ${header.values[1]}`).toBe(true);
    expect(sameEmail(header.values[3], testerEmail()), `paraplanner email is ${header.values[3]}`).toBe(true);
  });

  test('5. answer the Tax review - Clear empties the rich-text answer - and submit it on to AQS', async ({ page }) => {
    const reviewId = need(run.taxReviewId, 'the Tax review id');
    await openEditableReview(page, reviewId, ref);

    // "For Tax team usage": on to AQS, which is what the route already says.
    const disposition = page.locator('#ot-taxdisposition');
    await expect(disposition, 'the Tax review offers "For Tax team usage"').toBeVisible();
    await withSave(page, page.locator('#ot-header-status'), 'For Tax team usage', async () => {
      await disposition.selectOption('120910570'); // Submit to AQS
    });

    // The rich-text answer's Clear button empties it - and the empty answer is saved.
    const remedial = page.locator('[data-ot-answer][data-question-code="Q-TAX-04"]');
    await expect(remedial, 'the Tax review carries its rich-text answer (Tax Remedial)').toHaveCount(1);
    const area = remedial.locator('[data-ot-rich]');
    const status = remedial.locator('[data-ot-status]');
    await withSave(page, status, 'Tax Remedial', () => area.fill(`${NOTE} (text to be cleared)`));
    await withSave(page, status, 'Tax Remedial, cleared', () => remedial.locator('[data-ot-rte-clear]').click());
    expect((await area.textContent())?.trim() ?? '', 'Clear left text in the answer').toBe('');
    await withSave(page, status, 'Tax Remedial', () => area.fill(NOTE));

    const answered = await answerReview(page, NOTE);
    log?.note(`Tax review answered: ${answered.map((a) => `${a.code}=${a.answer}`).join(', ')}`);
    await writeRemedialActions(page, NOTE); // owed only on a Tax fail; none expected here

    await submitReview(page, NOTE, (line) => log?.note(line));
    await expect(page.locator('[data-ot-review-status]')).toContainText('Submitted');

    // The case goes back to the queue for its AQS check.
    const frame = await openCodeApp(page);
    await waitCaseStatus(frame, need(run.caseId, 'the case id'), ['Queued']);
  });

  test('6. allocate the AQS check to the tester in the Code App', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');
    const frame = await openCodeApp(page);
    const check = await allocateCheck(frame, caseId, 'AQS', testerEmail(), NOTE);
    run.aqsReviewId = check.id;
    log?.set({ aqsReviewId: check.id });
    log?.note(`AQS check ${check.reference} allocated; owner shown as "${check.owner}"`);
  });

  test('7. answer the AQS review with a failing grade, write its remedial action, and submit', async ({ page }) => {
    const reviewId = need(run.aqsReviewId, 'the AQS review id');
    await openEditableReview(page, reviewId, ref);

    const answered = await answerReview(page, NOTE);
    log?.note(`AQS review answered: ${answered.map((a) => `${a.code}=${a.answer}`).join(', ')}`);
    const grade = answered.find((a) => a.code === 'Q-GR-01');
    expect(grade?.answer, 'the grade was answered with a non-pass').toMatch(/^1209103(02|03|04)$/);

    // A non-pass grade owes a remediation: the card shows, and every row needs words.
    await expect(page.locator('[data-ot-remedial]'), 'a failing grade shows "Fail points and remedial actions"').toBeVisible();
    expect(await writeRemedialActions(page, NOTE)).toBeGreaterThan(0);

    await submitReview(page, NOTE, (line) => log?.note(line));
    await expect(page.locator('[data-ot-review-status]')).toContainText('Submitted');

    const frame = await openCodeApp(page);
    const status = await waitCaseStatus(frame, need(run.caseId, 'the case id'), ['Awaiting Remediation', 'Remediation In Progress']);
    log?.note(`after the AQS submit the case is ${status}`);
  });

  test('8. the remediation action is the tester\'s, by email, and on their remediation list', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');
    const name = need(run.testerName, 'the tester name');

    // On the list, found by its reference.
    await pollPortal(page, `case ${ref} on the remediation list`, (attempt) => remediationListUrl(ref, attempt), async () =>
      (await page.locator('table.ot-table tbody th a', { hasText: ref }).count()) > 0);

    // On the case: the owner is the contact holding the email - not the name on the case,
    // which is deliberately different - and the answer controls are drawn for this session
    // only because the action is assigned to it.
    await pollPortal(page, 'the remediation action, assigned to the tester', (attempt) => remediationCaseUrl(caseId, attempt), async () =>
      (await page.locator('[data-ot-performed], [data-ot-response]').count()) > 0);
    await expect(page.locator('.ot-notice').first()).toContainText(`Remediation for ${ref}.`);
    await expect(page.locator('.ot-unassigned'), 'no action is unassigned').toHaveCount(0);
    const owner = page.locator('table', { has: page.locator('th', { hasText: 'Action performed' }) }).first().locator('tbody tr', { hasText: name });
    await expect(owner.first(), `an action whose Owner is ${name}`).toBeVisible();
  });

  test('9. the adviser answers the action and completes the remediation', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');
    await page.goto(remediationCaseUrl(caseId, 50));
    await expectSignedIn(page, portalBase());
    await expectNoLiquidError(page);

    const performed = page.locator('[data-ot-performed]');
    const freeText = page.locator('[data-ot-response]');
    if ((await performed.count()) > 0) {
      for (let i = 0; i < (await performed.count()); i += 1) {
        const cell = performed.nth(i);
        await cell.locator('input[data-ot-performed-choice][value="120910815"]').check(); // Yes
        await cell.locator('[data-ot-performed-note]').fill(NOTE);
      }
    } else {
      await expect(freeText.first(), 'an action the tester can answer').toBeVisible();
      for (let i = 0; i < (await freeText.count()); i += 1) {
        await freeText.nth(i).locator('[data-ot-response-text]').fill(NOTE);
      }
    }
    await page.locator('input[data-ot-fq="clientcontact"][value="120910794"]').check(); // Client contact required? No

    await page.locator('[data-ot-form-submit]').click();
    const status = page.locator('[data-ot-form-status]');
    await expect(status).not.toHaveText('', { timeout: 120_000 });
    await expect(status, 'the completion was accepted').toContainText(/^Signed off\. \d+ remedial action/, { timeout: 120_000 });
  });

  test('10. the T&C Manager signs the remediation off, with a recheck', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');

    await pollPortal(page, 'the T&C sign-off form', (attempt) => remediationCaseUrl(caseId, attempt + 100), async () =>
      (await page.locator('[data-ot-signoff]').count()) > 0
      || (await page.getByText('mapped to its adviser, which your account is not for this one').count()) > 0);
    await expect(
      page.locator('[data-ot-signoff]'),
      'the sign-off is withheld: the tester is not the T&C Manager mapped to the adviser email',
    ).toBeVisible();

    await page.locator('#ot-decision').selectOption('120910720'); // Approved
    if ((await page.locator('#ot-signoff-outcome').count()) > 0) {
      await page.locator('#ot-signoff-outcome').selectOption(''); // leave for the regrade
    }
    await page.locator('#ot-recheck').selectOption('120910796'); // Recheck required: Yes
    await page.locator('#ot-changesadvice').selectOption('120910799'); // Changes the advice: No
    await page.locator('#ot-notes').fill(NOTE);
    await page.locator('[data-ot-signoff-save]').click();

    const status = page.locator('[data-ot-signoff-status]');
    await expect(status, 'the sign-off was recorded').toContainText(/^Approved, recorded against \d+ remedial action/, { timeout: 120_000 });
  });

  test('11. the regrade records the final outcome and closes the case', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');

    await pollPortal(page, 'the regrade form (case at Awaiting Recheck)', (attempt) => remediationCaseUrl(caseId, attempt + 200), async () =>
      (await page.locator('[data-ot-regrade]').count()) > 0);

    await page.locator('#ot-regrade-outcome').selectOption('Pass');
    await page.locator('#ot-regrade-reason').fill(NOTE);
    await page.locator('[data-ot-regrade-save]').click();
    await expect(page.locator('[data-ot-regrade-status]'), 'the regrade was recorded').toContainText('Final outcome recorded as Pass', { timeout: 120_000 });

    await pollPortal(page, 'the case closed on the portal', (attempt) => remediationCaseUrl(caseId, attempt + 300), async () =>
      /Closed/i.test(await portalCaseStatus(page)));
  });

  test('12. the Code App shows the case Closed, the tester once on People, and the mapping by adviser or manager', async ({ page }) => {
    const caseId = need(run.caseId, 'the case id');
    const email = testerEmail();
    const name = need(run.testerName, 'the tester name');

    const frame = await openCodeApp(page);
    await waitCaseStatus(frame, caseId, ['Closed']);
    expect(await summaryValue(frame, 'Reference')).toBe(ref);
    const checks = frame.locator('table.case-detail__checks-table tbody tr');
    await expect(checks, 'the Tax and the AQS check').toHaveCount(2);

    await reopen(frame, '#/admin/people');
    await frame.locator('#people-search').fill(email);
    await expect.poll(async () => (await peopleRowsFor(frame, email)).length, {
      message: `People lists ${email} once, although the case names "${ADVISER_NAME}" and "${PARAPLANNER_NAME}"`, timeout: 60_000,
    }).toBe(1);

    await expectMappedToSelf(frame, email, name);
    // ...and by the manager's name, the other half of the search.
    await frame.locator('#advisers-search').fill(name);
    await expect.poll(async () => (await mappingRows(frame)).some((row) => sameEmail(row.adviserEmail, email) && row.manager === name), {
      message: `searching the mapping for the manager "${name}" finds the tester's own mapping`, timeout: 30_000,
    }).toBe(true);

    log?.note(`case ${ref} (${caseId}) closed; client "${CLIENT_NAME}"`);
  });
});

/* ------------------------------------------------------------ Code App reads */

async function peopleRowsFor(frame: Frame, email: string): Promise<{ name: string; email: string }[]> {
  const rows = await frame.locator('table.people tbody tr').evaluateAll((trs) =>
    trs.map((tr) => ({
      name: (tr.querySelector('th')?.textContent ?? '').trim(),
      email: (tr.querySelector('td')?.textContent ?? '').trim(),
    })));
  return rows.filter((row) => sameEmail(row.email, email));
}

async function mappingRows(frame: Frame): Promise<{ adviserEmail: string; manager: string }[]> {
  return frame.locator('table.advisers__table tbody tr').evaluateAll((trs) =>
    trs.map((tr) => ({
      adviserEmail: (tr.querySelector('.advisers__email')?.textContent ?? '').trim(),
      manager: (tr.querySelectorAll('td')[1]?.textContent ?? '').trim(),
    })));
}

/** Adviser mapping: the tester's email maps to the tester as T&C Manager, found by the email. */
async function expectMappedToSelf(frame: Frame, email: string, name: string): Promise<void> {
  await reopen(frame, '#/admin/advisers');
  await frame.locator('#advisers-search, section.page-unavailable').first().waitFor({ timeout: 60_000 });
  await expectNotUnavailable(frame, 'Adviser mapping');
  await frame.locator('#advisers-search').fill(email);
  await expect.poll(async () => {
    const mine = (await mappingRows(frame)).filter((row) => sameEmail(row.adviserEmail, email));
    return mine.length === 0 ? 'no mapping for the tester\'s email' : mine.map((row) => row.manager).join(' | ');
  }, {
    message: `a T&C mapping whose adviser is ${email} and whose manager is ${name} - without it the sign-off is refused`,
    timeout: 60_000,
  }).toBe(name);
}
