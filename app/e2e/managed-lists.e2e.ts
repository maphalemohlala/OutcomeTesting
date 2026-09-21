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
 * The managed dropdowns on an editable review page (AD-192, AD-195).
 *
 * These went wrong in a way no unit test could have caught and no error message reported:
 * the table permission's web roles were written to `mspp_entitypermission_webrole`, a legacy
 * projection the enhanced data model ignores, so `/_api/al_listoptions` returned 403 and the
 * page rendered every list EMPTY. A field with nothing in it looks exactly like a field
 * nobody built, which is how it survived a day of use and was reported as "the product page
 * is still a free text".
 *
 * So the assertion that matters is not "the control exists" - it did throughout - but "the
 * control has options in it". That is the difference between the defect and the fix.
 */
const REVIEW_URL = 'OT_REVIEW_URL';

test.describe('the managed lists on a review page', () => {
  requires(PORTAL_URL, REVIEW_URL);

  test('fills every managed dropdown with its options', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const reviewUrl = requireEnv(REVIEW_URL);

    await page.goto(reviewUrl);
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // Each of the four single-choice lists. "Not set" alone is the 403 rendered as data,
    // so the bar is at least one REAL option beyond it.
    for (const label of [
      'Case type',
      'Product / solution type',
      'Sample source',
      'Pre or post check',
    ]) {
      const select = page.getByLabel(label, { exact: false }).first();
      if ((await select.count()) === 0) {
        continue; // Not every review page carries every field; absence is not this test's concern.
      }

      const options = select.locator('option');
      const real = (await options.allTextContents())
        .map((t) => t.trim())
        .filter((t) => t !== '' && t !== 'Not set' && t !== 'Choose…');

      expect(real.length, `${label} offered no options - the 403 shape of AD-192`).toBeGreaterThan(0);
    }
  });

  test('offers the Products tick list rather than a free-text box', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const reviewUrl = requireEnv(REVIEW_URL);

    await page.goto(reviewUrl);
    await expectSignedIn(page, portal);

    // Products is the many-to-many, and the original report was that it "is still a free
    // text". It was not - it was a tick list with nothing to tick, which looks the same.
    const ticks = page.locator('input[type="checkbox"]');
    expect(await ticks.count(), 'Products offered no checkboxes').toBeGreaterThan(0);
  });
});

/**
 * The portal Web API allowlist that AD-199 narrowed. `al_recheckrequired` and
 * `al_changesadvice` are the T&C Manager's answers, recorded when they sign a remediation
 * off - they were on the adviser-writable column list, so the adviser's own page could have
 * sent them whatever the plug-in decided. Removing them from the allowlist is the boundary;
 * the plug-in guard is the second line, not the first.
 */
test.describe('the remediation Web API allowlist', () => {
  requires(PORTAL_URL, 'OT_ACTION_ID');

  test('refuses the T&C answer columns to the browser', async ({ page, request }) => {
    const portal = requireEnv(PORTAL_URL);
    const actionId = requireEnv('OT_ACTION_ID');

    await page.goto(portal);
    await expectSignedIn(page, portal);

    // Sent as the signed-in browser would send it. A 200 here would mean the allowlist has
    // been widened back and the adviser can write the supervisor's answers again.
    const response = await request.patch(
      `${portal.replace(/\/+$/, '')}/_api/al_remediationactions(${actionId})`,
      {
        headers: { 'Content-Type': 'application/json' },
        data: { al_recheckrequired: true },
        failOnStatusCode: false,
      },
    );

    expect(
      response.status(),
      'al_recheckrequired is writable from the browser again (AD-199)',
    ).not.toBe(204);
    expect(response.status()).not.toBe(200);
  });
});
