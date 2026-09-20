import { describe, expect, it } from 'vitest';

/**
 * F27, found in DEV on 2026-09-20 while working APP-043.
 *
 * Allocating the AQS check on case 900000003 succeeded and said so — "AQS check allocated
 * — the checker will see it in their portal worklist." — while the Checks table directly
 * beneath the banner carried on listing one check. Leaving the page and coming back showed
 * both. The case header refreshed; the panels did not.
 *
 * `useCaseDetail` takes the page's reload key. `useCaseReviews` had no such parameter at
 * all, and `CaseOutcomeSummary` never passed one on. So a command that wrote a review
 * instance or an outcome left the page telling an administrator two different things about
 * the same case at the same moment — and the honest reading of "it says it worked but the
 * table has not changed" is that it did not work, which invites doing it twice.
 *
 * Checked at the source, because these hooks import `../../generated` and cannot be loaded
 * here. What matters is the wiring, and the wiring is what was missing.
 */
describe('everything on the case detail page re-reads after a command', () => {
  const sources = import.meta.glob('./CaseDetailPage.tsx', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  const page = Object.values(sources)[0] ?? '';

  it('is reading the page at all', () => {
    // Guards the test: a renamed or moved page would otherwise make it vacuously true.
    expect(page).toContain('useCaseDetail(');
    expect(page).toContain('const [reloadKey, setReloadKey]');
  });

  it('passes the reload key to every panel that reads case data', () => {
    // Each of these reads something a command on this page can change, and none of them
    // re-reads on its own. Any called with the case id alone will still be showing the
    // state from before.
    //
    // FailAccountabilityPanel is deliberately absent: it holds its own key and bumps it
    // after its own save, which is the only command that writes what it reads. Handing it
    // the page's key as well would make it re-read on every unrelated header edit.
    const readers = ['useCaseDetail(', 'useCaseReviews(', '<CaseOutcomeSummary'];

    const stale = readers
      .filter((reader) => page.includes(reader))
      .filter((reader) => {
        const at = page.indexOf(reader);
        // The call or the element, up to its close. Both forms end the argument list
        // before the next statement, so a short window is enough and cannot run on.
        const call = page.slice(at, at + 220);
        const upToEnd = call.slice(0, call.search(/\)\s*;|\/>/) + 2);
        return !upToEnd.includes('reloadKey');
      });

    expect(
      stale,
      'These read case data but are not told when a command has written something, so the ' +
        'page reports a success beside a panel still showing what was there before.',
    ).toEqual([]);
  });
});
