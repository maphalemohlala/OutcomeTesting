import { describe, expect, it } from 'vitest';
import { toReview } from './reviewRows';
import type { Al_reviewinstances } from '../../generated/models/Al_reviewinstancesModel';

/**
 * F26, found in DEV on 2026-09-20 while working APP-045.
 *
 * The Checks table on case 900000006 read `Tax check | Tax | Assigned | Unassigned` — the
 * Status column and the Owner column contradicting each other in the same row — and the
 * reallocation control offered "Leave as it is — Unassigned" for a check that was assigned,
 * had an active al_caseassignment row, and whose review instance was owned by the checker.
 *
 * The data was never wrong. The mapper read `owneridname`, which the Web API does not
 * return: a lookup's label arrives as an annotation on `_ownerid_value`. `lookupLabel` is
 * the helper caseDetailMapping and caseWorklistMapping already use for exactly this; this
 * one mapper did the read by hand.
 *
 * The third of this shape — F16 was the same read written by hand on the adviser mapping
 * table — so the last test here checks the CLASS rather than this instance.
 */

const FORMATTED = '_ownerid_value@OData.Community.Display.V1.FormattedValue';

function record(overrides: Record<string, unknown> = {}): Al_reviewinstances {
  return {
    al_reviewinstanceid: 'r1',
    al_reviewinstancecode: 'RI-TAX-0001',
    al_name: 'Tax check',
    al_sequence: 1,
    ...overrides,
  } as unknown as Al_reviewinstances;
}

describe('who holds a check', () => {
  it('reads the owner from the annotation the Web API actually sends', () => {
    expect(toReview(record({ [FORMATTED]: 'Service Account' })).owner).toBe('Service Account');
  });

  it('still reads owneridname where a read supplies one', () => {
    // FetchXML and the SDK do populate it. Dropping that fallback would trade one blank
    // column for another.
    expect(toReview(record({ owneridname: 'Simunye Radingwana' })).owner).toBe(
      'Simunye Radingwana',
    );
  });

  it('prefers the annotation, which is the fresher of the two', () => {
    expect(
      toReview(record({ [FORMATTED]: 'Service Account', owneridname: 'Someone Else' })).owner,
    ).toBe('Service Account');
  });

  it('says nobody only when the record really names nobody', () => {
    expect(toReview(record()).owner).toBeNull();
    expect(toReview(record({ [FORMATTED]: '   ' })).owner).toBeNull();
  });
});

/**
 * The class, not the instance.
 *
 * Every lookup label on these mappers has to come from `lookupLabel`, because the generated
 * `<attribute>name` field is empty on this app's reads and reading it by hand produces a
 * column that is silently blank — which is what F16 and F26 both were. A `record.xidname`
 * left outside a `lookupLabel(...)` call is that bug being written a third time.
 */
describe('lookup labels across the case mappers', () => {
  const sources = import.meta.glob('./*.ts', {
    query: '?raw',
    import: 'default',
    eager: true,
  }) as Record<string, string>;

  it('reads every lookup name through lookupLabel', () => {
    const offenders: string[] = [];

    for (const [path, source] of Object.entries(sources)) {
      if (path.endsWith('.test.ts') || path.endsWith('lookupLabel.ts')) continue;

      // The helper takes the generated field as its own fallback argument, so its call
      // sites legitimately mention it. Remove the calls, then look at what is left.
      const outside = source.replace(/lookupLabel\([^)]*\)/g, '');

      for (const match of outside.matchAll(/record\.(\w+idname)\b/g)) {
        offenders.push(`${path}: ${match[0]}`);
      }
    }

    expect(
      offenders,
      'These read a lookup label the Web API does not return, so the column renders blank ' +
        'however the row is set. Use lookupLabel(record, "<attribute>", record.<attribute>name).',
    ).toEqual([]);
  });

  it('is looking at the mappers at all', () => {
    // Guards the test: a moved file or a changed glob would otherwise make it vacuous.
    expect(Object.keys(sources).length).toBeGreaterThan(10);
    expect(Object.values(sources).join('')).toContain('lookupLabel(');
  });
});
