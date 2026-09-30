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
 * The AQS checker's Case Notes and Even Better If, as the adviser reads them (2026-09-30).
 * Read-only: nothing here writes.
 *
 * `OT_NOTES_CASE_ID` must be a case the session can open whose submitted AQS check has both
 * notes filled in (on DEV, 920929001: 281e5a2a-ffbb-f111-aaad-e4fade0775c0).
 *
 * The assertion that matters is that the NOTE TEXT is on the page, not that the panel is.
 * The first version drew both labels over empty boxes: a plain-text answer has no rich text,
 * the column comes back null, and on this site null != '' is true. The Tax panel had done
 * the same to every Tax "Case notes" since it was built.
 */
const CASE_ID = 'OT_NOTES_CASE_ID';

async function notesOn(page: import('@playwright/test').Page, selector: string) {
  const panel = page.locator(selector);
  await expect(panel).toHaveCount(1);
  return panel.locator('.ot-tax-notes__text').allTextContents();
}

test.describe('the AQS check notes, for the adviser', () => {
  requires(PORTAL_URL, CASE_ID);

  for (const [where, path] of [
    ['the Remediation page', 'remediation?case='],
    ['the case record', 'case-details?id='],
  ] as const) {
    test(`shows the AQS checker's words on ${where}`, async ({ page }) => {
      const portal = requireEnv(PORTAL_URL);
      await page.goto(`${portal.replace(/\/$/, '')}/${path}${requireEnv(CASE_ID)}`);
      await expectSignedIn(page, portal);
      await expectNoLiquidError(page);

      const panel = page.locator('section.ot-aqs-notes');
      await expect(panel.getByText('Case Notes', { exact: true })).toBeVisible();
      await expect(panel.getByText('Even Better If...', { exact: true })).toBeVisible();

      const notes = await notesOn(page, 'section.ot-aqs-notes');
      expect(notes).toHaveLength(2);
      for (const note of notes) {
        expect(note.trim(), 'a labelled note with nothing under it').not.toBe('');
      }
    });
  }

  test('shows every Tax note it labels, on the Remediation page', async ({ page }) => {
    const portal = requireEnv(PORTAL_URL);
    await page.goto(`${portal.replace(/\/$/, '')}/remediation?case=${requireEnv(CASE_ID)}`);
    await expectSignedIn(page, portal);

    const tax = page.locator('section.ot-tax-notes:not(.ot-aqs-notes)');
    if ((await tax.count()) === 0) {
      test.skip(true, 'this case has no Tax notes; the Tax half of the null fix is not exercised');
      return;
    }
    for (const note of await tax.locator('.ot-tax-notes__text').allTextContents()) {
      expect(note.trim(), 'a labelled Tax note with nothing under it').not.toBe('');
    }
  });
});
