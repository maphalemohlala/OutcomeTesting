import { readdirSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import type { Frame, Page } from '@playwright/test';
import { env, expect, requireEnv, requires, test } from './portal';

/**
 * The Code App after the 2026-09-24 feedback batch, in a browser (AD-219).
 *
 * The Code App runs inside the Power Apps player, two iframes deep, and signs in through the
 * same Entra tenant as the portal - so the portal session file carries the Microsoft cookies
 * it needs and no second sign-in is captured. The app uses a HashRouter, so each page is
 * reached by setting the app frame's hash rather than by a URL the player would not accept.
 *
 *   OT_CODEAPP_URL   the player URL: https://apps.powerapps.com/play/e/<env>/app/<appId>
 *   OT_REVIEW_ID     an AQS review to open read-only (defaults to nothing: that test skips)
 *   OT_PERSON_NAME   a checker with cases, for the person page (optional)
 *
 * Read-only. Nothing here writes.
 */
const CODEAPP_URL = 'OT_CODEAPP_URL';

// Loaded as an ES module, where __dirname does not exist - the config derives it the same way.
const HERE = dirname(fileURLToPath(import.meta.url));

/** The frame the app itself runs in: the one holding React's root. */
async function appFrame(page: Page): Promise<Frame> {
  await page.goto(requireEnv(CODEAPP_URL));
  let found: Frame | undefined;
  await expect
    .poll(
      async () => {
        for (const frame of page.frames()) {
          const hasRoot = await frame.evaluate(() => !!document.querySelector('#root nav, #root main')).catch(() => false);
          if (hasRoot) { found = frame; return true; }
        }
        return false;
      },
      { timeout: 60_000, message: 'the Code App never rendered - the Entra session may have expired' },
    )
    .toBe(true);
  return found as Frame;
}

async function go(frame: Frame, hash: string): Promise<void> {
  await frame.evaluate((target) => { window.location.hash = target; }, hash);
}

async function columnHeadings(frame: Frame, tableSelector: string): Promise<string[]> {
  await frame.locator(`${tableSelector} thead th`).first().waitFor({ timeout: 30_000 });
  return (await frame.locator(`${tableSelector} thead th`).allTextContents()).map((text) => text.trim());
}

test.describe('the Code App after the 2026-09-24 feedback', () => {
  requires(CODEAPP_URL);
  test.setTimeout(120_000);

  test('serves the bundle that was built and pushed', async ({ page }) => {
    const frame = await appFrame(page);
    const built = readdirSync(resolve(HERE, '../dist/assets')).find((file) => /^index-.*\.js$/.test(file));
    const served = await frame.evaluate(() => [...document.scripts].map((script) => script.src).join(' '));
    expect(built, 'app/dist holds a built bundle').toBeTruthy();
    expect(served, 'the player serves the bundle in app/dist - not a stale one').toContain(built as string);
  });

  test('the case worklist names the Tax and AQS checker', async ({ page }) => {
    const frame = await appFrame(page);
    await go(frame, '#/cases');
    const heads = await columnHeadings(frame, 'table');
    expect(heads).toEqual(expect.arrayContaining(['Tax checker', 'AQS checker']));
  });

  test('the completed case report names the Tax and AQS checker', async ({ page }) => {
    const frame = await appFrame(page);
    await go(frame, '#/reports');
    const heads = await columnHeadings(frame, 'table.completed__table');
    expect(heads).toEqual(expect.arrayContaining(['Tax checker', 'AQS checker']));
  });

  test('a person’s cases name the Tax and AQS checker', async ({ page }) => {
    const name = env('OT_PERSON_NAME');
    test.skip(!name, 'set OT_PERSON_NAME to a checker who holds cases');
    const frame = await appFrame(page);
    await go(frame, `#/people/Checker/${encodeURIComponent(name as string)}`);
    const heads = await columnHeadings(frame, 'table.people');
    expect(heads).toEqual(expect.arrayContaining(['Tax checker', 'AQS checker']));
  });

  test('the question library knows the two new response types', async ({ page }) => {
    const frame = await appFrame(page);
    await go(frame, '#/admin/questions');
    await expect(frame.locator('body')).toContainText('Pass / Fail / Insufficient evidence / N/A', { timeout: 30_000 });
    await expect(frame.locator('body')).toContainText('Multi select (root causes)');
  });

  test('a review reads with N/A columns and subsections headed by name', async ({ page }) => {
    const reviewId = env('OT_REVIEW_ID');
    test.skip(!reviewId, 'set OT_REVIEW_ID to an AQS review');
    const frame = await appFrame(page);
    await go(frame, `#/reviews/${reviewId}/aqs`);

    await frame.locator('table.grid thead').first().waitFor({ timeout: 30_000 });
    const heads = await frame.locator('table.grid thead tr').evaluateAll((rows) =>
      rows.map((row) => [...row.querySelectorAll('th')].map((th) => th.textContent?.trim())),
    );
    expect(heads).toContainEqual(['Suitability test point', 'Pass', 'Fail', 'Insufficient evidence', 'N/A']);
    expect(heads).toContainEqual(['Centralised Retirement Proposition test point', 'Pass', 'Fail', 'Insufficient evidence', 'N/A']);

    // N/A takes the same share as its neighbours: at 15% a column the four claimed 115%, and
    // the browser left the last one at its minimum width.
    const widths = await frame.locator('table.grid', { hasText: 'Centralised Retirement Proposition test point' })
      .locator('thead th').evaluateAll((ths) => ths.map((th) => th.getBoundingClientRect().width));
    expect(Math.abs(widths[4] - widths[1]), `N/A ${widths[4]}px against Pass ${widths[1]}px`).toBeLessThan(4);

    const bands = (await frame.locator('tr.section td').allTextContents()).map((text) => text.trim());
    expect(bands).toContain('Client Objectives & Information (COBS 9.2)');
    expect(bands.some((band) => /^E\d\./.test(band))).toBe(false);

    await expect(frame.locator('body')).not.toContainText('Record any detail once in section H');

    // Only the concessions row offers N/A inside Suitability; every CRP row does.
    const naBoxes = (label: string) =>
      frame.locator('tr', { hasText: label }).locator('.cc-box[aria-label^="N/A"]');
    await expect(naBoxes('Any concessions / off-tariff pricing approved and recorded')).toHaveCount(1);
    await expect(naBoxes('Adviser charges clearly disclosed and evidenced')).toHaveCount(0);
    await expect(naBoxes('Cashflow model stress tests on file')).toHaveCount(1);
  });
});
