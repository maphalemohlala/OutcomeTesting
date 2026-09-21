import { describe, expect, it } from 'vitest';

/**
 * A page somebody cannot open says why, and does not say anything else.
 *
 * F42, seen on 2026-09-21 driving the role-separation rows as a Tax Reviewer. The refusal
 * panel was `NotBuiltYet`, whose explanation was a fixed sentence written for screens that
 * genuinely had no data source: "This screen has no data source. The Dataverse tables it
 * depends on have not been created." By then it had no such callers left. Opening Exports
 * without the role produced both of these, one line apart, in the same panel:
 *
 *   "Ask an administrator to assign it in Security configuration."
 *   "The Dataverse tables it depends on have not been created."
 *
 * Only the first is true. The second sends the person - and whoever they take it to - to
 * look for a broken deployment instead of a missing role, and it was doing the same on the
 * page-not-found route.
 *
 * `detail` is a required prop now, so the compiler stops a caller inheriting someone else's
 * explanation. This guards the other half: that the retired sentence does not come back, in
 * that panel or any other.
 */
describe('a blocked page explains itself honestly', () => {
  // Every source file in the app, as text. Deliberately the whole tree rather than the two
  // call sites: the sentence was wrong wherever it appeared, and the next place it could
  // appear is one nobody has written yet.
  const sources = import.meta.glob('../../**/*.{ts,tsx}', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  it('is looking at something', () => {
    expect(Object.keys(sources).length).toBeGreaterThan(50);
  });

  it('never tells anyone the tables have not been created', () => {
    const offenders = Object.entries(sources)
      .filter(([path]) => !path.includes('pageUnavailableTellsTheTruth'))
      .filter(([, text]) => /tables it depends on have not been created|screen has no data source/i.test(text))
      .map(([path]) => path);

    expect(
      offenders,
      'This sentence was written for screens that really had no data source, and there are '
        + 'none left. Wherever it appears now it is telling somebody the deployment is '
        + 'broken when the real answer is that they lack a role, or followed a bad link '
        + '(F42). Say what is actually true of that page instead.',
    ).toEqual([]);
  });
});
