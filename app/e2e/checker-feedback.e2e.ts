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
 * The checker feedback batch of 2026-09-24, in a browser (AD-219).
 *
 * The vitest suite pins the Liquid source; this proves the page a checker opens: subsections
 * headed by name alone, the N/A columns on Suitability and CRP, no Consumer Duty intro, the
 * way back to the queue, the IO reference, several root causes, and who each case is
 * assigned to on the lists.
 *
 * `OT_REVIEW_URL` must be an EDITABLE review - assigned to the session's account, not yet
 * submitted - opened on or after 2026-09-24, so it reads Q-GR-02's multi-select version.
 *
 * Writes are OPT-IN (`OT_ALLOW_WRITES=1`). With them, the spec saves N/A on a CRP row and two
 * root causes through the page's own autosave, which is the path that reaches
 * AnswerRequestPlugin and ResponseRules - a "Saved" status is the server accepting the new
 * response types. The root causes are unticked again afterwards; the N/A stays as the answer.
 */
const REVIEW_URL = 'OT_REVIEW_URL';

function watchConsole(page: import('@playwright/test').Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') { errors.push(message.text()); }
  });
  page.on('pageerror', (error) => { errors.push(String(error)); });
  return errors;
}

test.describe('the review page after the 2026-09-24 feedback', () => {
  requires(PORTAL_URL, REVIEW_URL);

  test.beforeEach(async ({ page }) => {
    await page.goto(requireEnv(REVIEW_URL));
    await expectSignedIn(page, requireEnv(PORTAL_URL));
    await expectNoLiquidError(page);
  });

  test('heads the Suitability subsections by name alone', async ({ page }) => {
    const bands = await page.locator('tr.section td').allTextContents();
    expect(bands.map((band) => band.trim())).toContain('Client Objectives & Information (COBS 9.2)');
    expect(bands.some((band) => /^E\d\./.test(band.trim()))).toBe(false);
  });

  test('gives Suitability and CRP an N/A column, ticked only where the row offers it', async ({ page }) => {
    const heads = await page.locator('table.grid thead tr').evaluateAll((rows) =>
      rows.map((row) => [...row.querySelectorAll('th')].map((th) => th.textContent?.trim())),
    );
    expect(heads).toContainEqual(['Suitability test point', 'Pass', 'Fail', 'Insufficient evidence', 'N/A']);
    expect(heads).toContainEqual(['Centralised Retirement Proposition test point', 'Pass', 'Fail', 'Insufficient evidence', 'N/A']);

    // N/A takes the same share as its neighbours: at 15% a column the four claimed 115%, and
    // the browser left the last one at its minimum width.
    const widths = await page.locator('table.grid', { hasText: 'Centralised Retirement Proposition test point' })
      .locator('thead th').evaluateAll((ths) => ths.map((th) => th.getBoundingClientRect().width));
    expect(Math.abs(widths[4] - widths[1]), `N/A ${widths[4]}px against Pass ${widths[1]}px`).toBeLessThan(4);

    await expect(page.locator('tr[data-question-code="Q-E4-03"] input[value="120910307"]')).toHaveCount(1);
    await expect(page.locator('tr[data-question-code="Q-E4-01"] input[value="120910307"]')).toHaveCount(0);
    for (const code of ['Q-CRP-01', 'Q-CRP-02', 'Q-CRP-03', 'Q-CRP-04']) {
      await expect(page.locator(`tr[data-question-code="${code}"] input[value="120910307"]`)).toHaveCount(1);
    }
  });

  test('prints no Consumer Duty intro line', async ({ page }) => {
    const heading = page.locator('h2', { hasText: /^Consumer Duty overlay$/ });
    await expect(heading).toHaveCount(1);
    await expect(page.locator('body')).not.toContainText('Record any detail once in section H');
  });

  test('offers a way back to the queue at the foot of the check', async ({ page }) => {
    const back = page.getByRole('link', { name: 'Back to your queue' });
    await expect(back).toBeVisible();
    expect(await back.getAttribute('href')).toMatch(/^\/(aqs|tax)-reviews$/);
  });

  test('shows the IO reference, not the case reference, under IO reference', async ({ page }) => {
    const io = await page.locator('td.lbl', { hasText: /^IO reference$/ }).locator('xpath=following-sibling::td[1]').textContent();
    const reference = await page.locator('[aria-labelledby="ot-review-summary"] dd a').first().textContent();
    expect((io ?? '').trim()).not.toBe('');
    expect((io ?? '').trim()).not.toBe((reference ?? '').trim());
  });

  test('draws the primary root cause as nine tick boxes that save as a set', async ({ page }) => {
    const table = page.locator('[data-ot-rootcause]');
    await expect(table).toHaveAttribute('data-column', 'choices');
    await expect(table.locator('input[type="checkbox"]')).toHaveCount(9);
    await expect(table.locator('input[type="radio"]')).toHaveCount(0);
  });
});

test.describe('saving the new answer types through the page', () => {
  requires(PORTAL_URL, REVIEW_URL);

  test('N/A on a CRP row and two root causes are accepted by the server', async ({ page }) => {
    test.skip(env('OT_ALLOW_WRITES') !== '1', 'writes are opt-in: set OT_ALLOW_WRITES=1');
    const errors = watchConsole(page);

    await page.goto(requireEnv(REVIEW_URL));
    await expectSignedIn(page, requireEnv(PORTAL_URL));

    // N/A on the first CRP row.
    const crp = page.locator('tr[data-question-code="Q-CRP-01"]');
    const status = crp.locator('[data-ot-status]');
    // Re-runnable: ticking an N/A that is already the answer changes nothing and saves
    // nothing, so a second run moves it to Pass first and then back.
    if (await crp.locator('input[value="120910307"]').isChecked()) {
      await crp.locator('input[value="120910300"]').check();
      await expect(status).toHaveText(/^Saved/, { timeout: 30_000 });
      await status.evaluate((element) => { element.textContent = ''; });
    }
    await crp.locator('input[value="120910307"]').check();
    await expect(crp.locator('[data-ot-status]')).toHaveText(/^Saved/, { timeout: 30_000 });

    // Two root causes. The table is shown only once the grade is not Pass, and the page hides
    // it again on every change while this review is ungraded - so both boxes are set in one
    // step and the change is raised on them directly, which is what reaches the autosave.
    // Nothing about the grade is changed.
    const table = page.locator('[data-ot-rootcause]');
    const tick = (checked: boolean) =>
      table.evaluate((element, on) => {
        for (const value of ['120910320', '120910326']) {
          const box = element.querySelector<HTMLInputElement>(`input[value="${value}"]`);
          if (box) { box.checked = on; }
        }
        element.querySelector('input')?.dispatchEvent(new Event('change', { bubbles: true }));
      }, checked);

    await tick(true);
    await expect(table.locator('[data-ot-status]')).toHaveText(/^Saved/, { timeout: 30_000 });

    // OT_KEEP_ROOT_CAUSES=1 leaves them saved, so the stored set can be read back from
    // Dataverse - answers carry no audit history to read it from instead.
    if (env('OT_KEEP_ROOT_CAUSES') === '1') {
      expect(errors, 'the answering script raised no errors').toEqual([]);
      return;
    }

    // And let them go again, so the review is left as it was found. The status is cleared
    // first, so the "Saved" awaited next is this save's and not the last one's.
    await table.locator('[data-ot-status]').evaluate((element) => { element.textContent = ''; });
    await tick(false);
    await expect(table.locator('[data-ot-status]')).toHaveText(/^Saved/, { timeout: 30_000 });

    expect(errors, 'the answering script raised no errors').toEqual([]);
  });
});

test.describe('who each case is assigned to', () => {
  requires(PORTAL_URL);

  for (const path of ['/', '/aqs-reviews', '/remediation']) {
    test(`${path} names the Tax and AQS checker`, async ({ page }) => {
      const portal = requireEnv(PORTAL_URL);
      await page.goto(`${portal}${path}`);
      await expectSignedIn(page, portal);
      await expectNoLiquidError(page);

      const tables = page.locator('table.ot-table');
      test.skip((await tables.count()) === 0, `${path} has no case table for this account right now`);
      await expect(page.locator('th', { hasText: /^Tax checker$/ }).first()).toBeVisible();
      await expect(page.locator('th', { hasText: /^AQS checker$/ }).first()).toBeVisible();
    });
  }
});
