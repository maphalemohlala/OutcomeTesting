import {
  PORTAL_URL,
  expect,
  expectNoLiquidError,
  expectSignedIn,
  requireEnv,
  requires,
  test,
} from './portal';

/**
 * The 2026-09-22 batch, in a browser: the widened case list, and the review form.
 *
 * The gating checks that used to live in the "the review form gating" describe below -
 * 'draws the fail points with a reason why they are locked', 'renders the lock server-side,
 * before any script runs' and 'takes Pass away without a reload when a finding is ticked' -
 * were retired on 2026-10-05, when the owner's direction (AD-231) stopped the form narrowing
 * answers at all: no lock, no greyed-out box, no automatic tick. Pinning rules the product no
 * longer has would fail the next deploy for doing exactly what was asked. See
 * `docs/superpowers/specs/2026-10-05-remediation-documents-and-open-form-design.md`.
 *
 * What is left answers what the vitest suite beside them cannot: whether the answering
 * script actually RUNS. A JavaScript error in it is silent - the page renders, every control
 * is present - which is the kind of failure this portal has shipped twice. So the console is
 * an assertion here, not a diagnostic.
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
});
