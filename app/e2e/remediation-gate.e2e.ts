import {
  CASE_MAPPED_ELSEWHERE,
  CASE_MAPPED_TO_ME,
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
 * The supervisor controls on OT Remediation, against a live portal (AD-198, AD-201, AD-202).
 *
 * The unit tests beside this read the template SOURCE and pin the Liquid. They cannot see
 * whether the table permission exists, whether contact scope resolves, or whether the fetch
 * returns anything - and every one of those was wrong at some point today. This spec is the
 * part that only a browser can answer.
 *
 * Both directions, always. A gate is only shown to work by a case it lets through AND a case
 * it stops; the half that passes proves the control exists, the half that fails proves it
 * discriminates. Asserting only the refusal would pass just as well against a page that
 * shows nobody anything.
 *
 * What this DOES NOT test is authorization. The server is the boundary - SignoffRequestPlugin
 * and RegradeRequestPlugin - and it is tested where it lives. This is about not offering
 * somebody a form they will be refused for filling in.
 */
test.describe('the supervisor controls on a remediation case', () => {
  requires(PORTAL_URL, CASE_MAPPED_TO_ME, CASE_MAPPED_ELSEWHERE);

  test('offers the sign-off form on a case whose adviser maps to me', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const caseId = requireEnv(CASE_MAPPED_TO_ME);

    await page.goto(remediationUrl(portal, caseId));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // The decision control is the form. Found by its label rather than by a class or a
    // data- attribute, because the label is what the supervisor is actually looking for.
    await expect(
      page.getByLabel('All remedial actions checked and approved?'),
    ).toBeVisible();

    await expect(page.locator('body')).not.toContainText('mapped to its adviser');
  });

  test('withholds BOTH supervisor controls on a case whose adviser maps to someone else', async ({
    page,
  }) => {
    const portal = requireEnv(PORTAL_URL);
    const caseId = requireEnv(CASE_MAPPED_ELSEWHERE);

    await page.goto(remediationUrl(portal, caseId));
    await expectSignedIn(page, portal);
    await expectNoLiquidError(page);

    // The sign-off half (AD-201).
    await expect(
      page.getByLabel('All remedial actions checked and approved?'),
    ).toHaveCount(0);

    // The regrade half (AD-202). This is the one that was open: the command checked the
    // role alone, so the form rendered here while the sign-off beside it was withheld,
    // and anyone holding the role could set the final outcome on anybody's case.
    await expect(page.getByRole('button', { name: 'Record the final outcome' })).toHaveCount(0);
    await expect(page.getByLabel('Reason (required)')).toHaveCount(0);
  });

  test('says why it is withheld rather than showing an empty page', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const caseId = requireEnv(CASE_MAPPED_ELSEWHERE);

    await page.goto(remediationUrl(portal, caseId));
    await expectSignedIn(page, portal);

    // A panel that simply vanishes reads as "there is nothing to do here", which is false:
    // there IS something to do and somebody else has to do it.
    await expect(page.locator('body')).toContainText(
      'Remedial actions on this case are waiting to be signed off',
    );
    await expect(page.locator('body')).toContainText('mapped to its adviser');
  });

  test('says only that it is not your case, naming nobody', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    const caseId = requireEnv(CASE_MAPPED_ELSEWHERE);

    await page.goto(remediationUrl(portal, caseId));
    await expectSignedIn(page, portal);

    // Who supervises whom is not this reader's business. The table permission is
    // contact-scoped so the page could not read the name anyway - this is the assertion
    // that notices if somebody ever widens the scope and starts printing it.
    //
    // Pinned as the WHOLE sentence rather than by hunting for name-shaped text. The first
    // attempt was /mapped to its adviser[^.]*\b(is|ask)\b/, which failed against the real
    // message - "...which your account IS not for this one" - and would have failed against
    // any correct wording containing those words. A proxy for "names a person" that cannot
    // say what a person looks like is not an assertion, it is a guess. Matching the exact
    // sentence means anything appended to it, a name or an address, breaks this test.
    const hint = page.locator('p', { hasText: 'mapped to its adviser' }).first();
    await expect(hint).toBeVisible();

    // Whitespace collapsed first. toHaveText does not normalise against a regex, and the
    // template wraps this sentence over three indented lines, so an un-normalised pattern
    // fails on the markup's shape rather than on its words.
    const text = (await hint.innerText()).replace(/\s+/g, ' ').trim();

    expect(text).toMatch(
      /^Remedial actions on this case are waiting to be signed off\. Signing a case off is the .+ mapped to its adviser, which your account is not for this one\.$/,
    );

    // And nothing address-shaped anywhere in it, which is the other way a name leaks.
    expect(await hint.innerText()).not.toContain('@');
  });
});
