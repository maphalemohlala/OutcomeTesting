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
 * The 2026-09-22 batch, in a browser: the widened case list, and the gating rules on the
 * review form.
 *
 * These answer what the vitest suite beside them cannot. That one reads the template source
 * and pins the Liquid; it cannot tell whether the rewritten answering script actually RUNS.
 * The script was substantially rewritten in this batch - a block that used to open with
 * `if (gradeRow)` now guards each rule separately, and three new ones were added - and a
 * JavaScript error in it is silent: the page renders, every control is present, and none of
 * the rules fire. That failure looks exactly like the old behaviour, which is the kind this
 * portal has shipped twice.
 *
 * So the console is an assertion here, not a diagnostic.
 *
 * Writes are OPT-IN. `OT_ALLOW_WRITES=1` lets the gating test tick a real answer on a real
 * review, which is the only way to prove the dynamic half; without it the spec checks
 * everything that can be checked without touching anyone's data. Default off, because these
 * run against environments other people are using.
 */
const REVIEW_URL = 'OT_REVIEW_URL';

/** Console errors, which on this page mean the answering script did not finish loading. */
function watchConsole(page: import('@playwright/test').Page): string[] {
  const errors: string[] = [];
  page.on('console', (message) => {
    if (message.type() === 'error') { errors.push(message.text()); }
  });
  page.on('pageerror', (error) => { errors.push(String(error)); });
  return errors;
}

test.describe('the case list asks for more width', () => {
  requires(PORTAL_URL);

  test('is wider than the shared reading width, and other pages are not', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);

    await page.goto(`${portal}/cases`);
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    const wide = await page.evaluate(() => {
      const inner = document.querySelector('.ot-page__inner');
      return inner ? getComputedStyle(inner).maxWidth : null;
    });

    expect(wide, 'the case list page has an .ot-page__inner').not.toBeNull();

    // 82rem at the site's root font size is 1312px; the wide rule is 110rem / 1760px. The
    // assertion is on the computed value rather than the rule text, because a stylesheet
    // cached at the old ?v= would serve the old rule and report success everywhere else.
    const px = Number.parseFloat(wide ?? '0');
    expect(px, `max-width was ${wide}; the wide rule did not apply`).toBeGreaterThan(1400);

    // The control. A page with no wide marker keeps the reading width, which is what makes
    // this a scoped change rather than a site-wide one.
    await page.goto(`${portal}/`);
    const home = await page.evaluate(() => {
      const inner = document.querySelector('.ot-page__inner');
      return inner ? Number.parseFloat(getComputedStyle(inner).maxWidth) : null;
    });

    if (home !== null && Number.isFinite(home)) {
      expect(home, 'the home page must keep the shared reading width').toBeLessThan(1400);
    }
  });
});

test.describe('the review form gating', () => {
  requires(PORTAL_URL, REVIEW_URL);

  test('loads the answering script without a console error', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const errors = watchConsole(page);

    await page.goto(requireEnv(REVIEW_URL));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // The markers every rule keys on. Their absence means the Liquid rendered but the rows
    // lost the attributes, and every rule would then read each row as a test point.
    const coded = await page.locator('[data-ot-answer][data-question-code]').count();
    expect(coded, 'answer rows carry their question code').toBeGreaterThan(0);

    expect(errors, `console errors on the review page:\n${errors.join('\n')}`).toEqual([]);
  });

  test('draws the fail points with a reason why they are locked', async ({ page }) => {
    await page.goto(requireEnv(REVIEW_URL));
    await expectNoLiquidError(page);

    const block = page.locator('[data-ot-failpoints-for]');
    if ((await block.count()) === 0) {
      test.skip(true, 'this review has no File Quality Outcome block, so no fail points');
      return;
    }

    // The note element must exist even when it has nothing to say - the script fills it.
    await expect(page.locator('[data-ot-failpoints-note]')).toHaveCount(1);

    // And the script must have decided something: either the boxes are open, or they are
    // shut AND the page says why. A locked block with no explanation is the reported bug.
    const state = await page.evaluate(() => {
      const box = document.querySelector('[data-ot-failpoints-for] [data-ot-reason]');
      const note = document.querySelector('[data-ot-failpoints-note]');
      return {
        locked: box ? (box as HTMLInputElement).disabled : null,
        reason: note ? (note.textContent ?? '').trim() : '',
      };
    });

    if (state.locked === true) {
      expect(state.reason, 'a locked fail points block says why').not.toEqual('');
    }
  });

  test('renders the lock server-side, before any script runs', async ({ page, request }) => {
    /*
     * The Liquid half, and the one a browser test would otherwise miss entirely: the page
     * calls syncGradeOptions() on load, so by the time Playwright can see the DOM the script
     * has already produced the same result the server should have. Both halves passing looks
     * identical to only the script working - and only the script working means a checker on a
     * slow connection sees Pass offered for a moment on a form that forbids it.
     *
     * So this reads the RAW HTML, where no script has run.
     */
    const reviewUrl = requireEnv(REVIEW_URL);
    const html = await (await request.get(reviewUrl)).text();

    const gradeRow = /<tr[^>]*data-response-type="120910010"[\s\S]*?<\/tr>/.exec(html);
    const fqRow = /<tr[^>]*data-question-code="Q-FQ(?:TAX)?-01"[\s\S]*?<\/tr>/.exec(html);
    const row = gradeRow?.[0] ?? fqRow?.[0];

    if (!row) {
      test.skip(true, 'this review draws neither the grade nor a file quality outcome');
      return;
    }

    // Does the page ALREADY hold a finding? If not there is nothing to have locked, and the
    // absence of a lock is the correct render rather than a failure.
    const hasFinding = /data-ot-answer[^>]*data-question-code="(?!Q-FQ-01|Q-FQTAX-01|Q-FQ-03|Q-FQTAX-03|Q-GR-01)[^"]+"[\s\S]{0,4000}?value="12091030(?:1)"[^>]*checked/.test(html)
      || /value="120910306"[^>]*checked/.test(html);

    if (!hasFinding) {
      test.skip(true, 'this review holds no No or Fail, so nothing should be locked yet');
      return;
    }

    const pass = /<input[^>]*value="120910300"[^>]*>/.exec(row);
    expect(pass, 'the outcome row offers a Pass option').not.toBeNull();
    expect(
      pass?.[0],
      `Pass was not disabled in the server-rendered HTML, so the Liquid pre-pass did not run: ${pass?.[0]}`,
    ).toContain('disabled');
  });

  test('takes Pass away without a reload when a finding is ticked', async ({ page }) => {
    if (env('OT_ALLOW_WRITES') === undefined) {
      test.skip(true, 'set OT_ALLOW_WRITES=1 to let this tick a real answer');
      return;
    }

    const errors = watchConsole(page);
    await page.goto(requireEnv(REVIEW_URL));
    await expectNoLiquidError(page);

    /*
     * The grade where there is one, and the file quality outcome otherwise.
     *
     * A TAX review has no grade at all - S-GRADE is AQS-owned and AD-020 filters it out - so
     * a probe that only knew about the grade skipped every Tax review silently, which is
     * exactly the half of this batch that needed proving in a browser.
     */
    const passOf = () =>
      page.evaluate(() => {
        const row =
          document.querySelector('[data-ot-answer][data-response-type="120910010"]')
          ?? document.querySelector('[data-ot-answer][data-question-code="Q-FQ-01"]')
          ?? document.querySelector('[data-ot-answer][data-question-code="Q-FQTAX-01"]');
        const pass = row?.querySelector('[data-ot-input][value="120910300"]');
        return pass ? (pass as HTMLInputElement).disabled : null;
      });

    const before = await passOf();
    if (before !== false) {
      // Already locked by a stored finding - which the test above covers. The dynamic half
      // needs a review with a Pass still available, and saying so is more use than a pass.
      test.skip(true, 'Pass is already locked on this review; point OT_REVIEW_URL at a clean one');
      return;
    }

    /*
     * The first test point offering a Fail or a No, skipping the outcome questions - which
     * are the ones the rule must NOT count, and ticking one here would prove the opposite of
     * what this asserts.
     */
    const target = await page.evaluate(() => {
      const OUTCOMES = '|Q-FQ-01|Q-FQTAX-01|Q-FQ-03|Q-FQTAX-03|Q-GR-01|';
      const rows = Array.from(document.querySelectorAll('[data-ot-answer][data-question-code]'));

      for (const row of rows) {
        const code = (row.getAttribute('data-question-code') ?? '').trim().toUpperCase();
        if (code === '' || OUTCOMES.indexOf(`|${code}|`) !== -1) { continue; }

        const inputs = Array.from(row.querySelectorAll('[data-ot-input]')) as HTMLInputElement[];
        const finding = inputs.find(
          (i) => (i.value === '120910301' || i.value === '120910306') && !i.disabled && !i.checked,
        );
        if (finding) {
          finding.id = finding.id || 'ot-e2e-finding';
          return finding.id;
        }
      }

      return null;
    });

    if (target === null) {
      test.skip(true, 'no unticked Fail or No is available on a test point of this review');
      return;
    }

    await page.locator(`#${target}`).check();

    // The rule runs on `change`, synchronously - no save has to land for the page to react,
    // which is the whole point of the client-side half (AD-041).
    await expect.poll(passOf).toBe(true);

    expect(errors, `console errors while gating: ${errors.join(' | ')}`).toEqual([]);
  });
});
