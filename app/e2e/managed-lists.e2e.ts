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

    /*
     * The four lists below are HEADER fields, and from 2026-09-22 the header is frozen once
     * the Tax check on the case has been submitted. On such a review none of them render at
     * all - correctly - and the tally at the end of this test then fails, blaming the lists
     * for a lock that is working.
     *
     * So the freeze is detected and named, rather than left to look like the AD-192 defect.
     * It is anchored on the editable control being ABSENT while the header itself is present,
     * which a page that failed to render would not satisfy.
     */
    const frozen = await page.evaluate(() =>
      document.querySelectorAll('[data-ot-hdr]').length === 0
      && /header is now read-only/i.test(document.body.innerText));

    if (frozen) {
      test.skip(true,
        'the header on this review is frozen because the Tax check has been submitted; '
        + 'point OT_REVIEW_URL at a review whose header is still editable');
      return;
    }

    // Each of the four single-choice lists. "Not set" alone is the 403 rendered as data,
    // so the bar is at least one REAL option beyond it.
    const checked: string[] = [];

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
      checked.push(label);
    }

    // The loop above skips a list it cannot find, so with none of the four present it would
    // assert nothing and pass - which is what AD-192 looked like from the outside, an empty
    // page reporting success. Third time this suite has made that mistake in a day: an
    // assertion phrased as an absence has to be anchored to something proved present.
    expect(
      checked,
      'none of the managed lists were on the page - is this an editable review assigned to you?',
    ).not.toHaveLength(0);
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
    const portal = requireEnv(PORTAL_URL).replace(/\/+$/, '');
    const actionId = requireEnv('OT_ACTION_ID');

    await page.goto(portal);
    await expectSignedIn(page, portal);

    // The anti-forgery token, the way the site's own scripts get it. Without it EVERY write
    // is refused, and this spec would then pass against a completely open allowlist - the
    // absence anchored to nothing, which is the mistake the session guard already made once
    // in this suite. Fetching it means the refusal below can only be about the columns.
    const tokenPage = await request.get(`${portal}/_layout/tokenhtml`);
    expect(tokenPage.status(), 'could not reach the anti-forgery token endpoint').toBe(200);
    const token = (await tokenPage.text()).match(
      /name="__RequestVerificationToken"[^>]*value="([^"]+)"/,
    )?.[1];
    expect(token, 'no __RequestVerificationToken in /_layout/tokenhtml').toBeTruthy();

    // And the record is readable, which proves the session, the id and the Web API are all
    // live before anything is read into a refusal.
    const read = await request.get(
      `${portal}/_api/al_remediationactions(${actionId})?$select=al_adviserresponse`,
      { failOnStatusCode: false },
    );
    expect(read.status(), 'the action is not readable - check OT_ACTION_ID').toBe(200);

    // Now the actual question. A 204 here means the adviser's own page can write the
    // T&C Manager's answers again (AD-199).
    const refused = await request.patch(`${portal}/_api/al_remediationactions(${actionId})`, {
      headers: {
        'Content-Type': 'application/json',
        __RequestVerificationToken: token as string,
      },
      data: { al_recheckrequired: true },
      failOnStatusCode: false,
    });

    // A refusal, not merely "not 204". `not.toBe(204)` would be satisfied by a 200, which
    // is the same shape of weak assertion as the session guard that let a signed-out run
    // pass - an expectation loose enough to be met by the thing it exists to catch.
    expect(
      refused.status(),
      'al_recheckrequired is writable from the browser again (AD-199)',
    ).toBeGreaterThanOrEqual(400);

    // ...and refused for the RIGHT reason. An anti-forgery failure here would mean the token
    // handling above broke and the allowlist was never exercised.
    const body = await refused.text();
    expect(body.toLowerCase()).not.toContain('anti-forgery');
    expect(body.toLowerCase()).not.toContain('requestverificationtoken');
  });
});
