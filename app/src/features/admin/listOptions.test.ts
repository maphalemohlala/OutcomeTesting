import { describe, expect, it } from 'vitest';
import {
  LIST_CASE_TYPE,
  LIST_PRE_OR_POST_CHECK,
  LIST_PRODUCT_SOLUTION_TYPE,
  LIST_SAMPLE_SOURCE,
  MANAGED_LISTS,
  MIGRATED_LISTS,
  type RawListOption,
  caseOptionLabel,
  listByKey,
  listByValue,
  lookupFormattedField,
  lookupValueField,
  offeredOptions,
  toOptionRows,
  validateDraft,
} from './listOptions';

/**
 * The four case-header dropdowns become rows an administrator can manage, starting with
 * Product / solution type (project owner, 2026-09-21: "How easy would it be to create a
 * management page where users can add, edit, and remove options?").
 *
 * These pin the three properties that make the migration safe: a case that still holds only
 * the old choice value keeps reading correctly, "remove" retires rather than deletes, and a
 * list nobody has migrated yet is not offered as though it worked.
 */

const PRODUCT = MANAGED_LISTS[0];
const DAY = new Date('2026-09-21T12:00:00Z');

function raw(over: Partial<RawListOption> & { al_listoptionid: string }): RawListOption {
  return { al_list: LIST_PRODUCT_SOLUTION_TYPE, ...over };
}

/** The five options DEV's choice column carries today, as rows. */
const SEED: RawListOption[] = [
  raw({ al_listoptionid: '1', al_name: 'Accumulation investment', al_sortorder: 10, al_legacyvalue: 120910520 }),
  raw({ al_listoptionid: '2', al_name: 'Accumulation Pension', al_sortorder: 20, al_legacyvalue: 120910521 }),
  raw({ al_listoptionid: '3', al_name: 'IHT', al_sortorder: 30, al_legacyvalue: 120910522 }),
  raw({ al_listoptionid: '4', al_name: 'Protection', al_sortorder: 40, al_legacyvalue: 120910523 }),
  raw({ al_listoptionid: '5', al_name: 'No change reviews', al_sortorder: 50, al_legacyvalue: 120910524 }),
];

describe('the managed lists', () => {
  it('names the four the request named, and no others', () => {
    expect(MANAGED_LISTS.map((list) => list.label)).toEqual([
      'Product / solution type',
      'Sample source',
      'Case type',
      'Pre or post check',
    ]);
  });

  it('allocates option values clear of every block already in use', () => {
    // 120910814 is the highest value allocated anywhere else in the solution. A collision
    // here would not fail loudly: it would make one list silently read as another.
    for (const list of MANAGED_LISTS) {
      expect(list.value, list.key).toBeGreaterThan(120910814);
    }
    expect(new Set(MANAGED_LISTS.map((l) => l.value)).size).toBe(MANAGED_LISTS.length);
  });

  it('offers only the lists a chosen option has somewhere to go', () => {
    // Product / solution type is the pilot. A list declared but not yet migrated has no
    // lookup on the case, so an option chosen for it could not be saved - offering it would
    // be a page that takes an administrator's work and drops it.
    expect(MIGRATED_LISTS.map((list) => list.value)).toEqual([LIST_PRODUCT_SOLUTION_TYPE]);
    for (const value of [LIST_SAMPLE_SOURCE, LIST_CASE_TYPE, LIST_PRE_OR_POST_CHECK]) {
      expect(listByValue(value)?.caseAttribute, String(value)).toBeNull();
    }
  });

  it('keeps naming the choice column each list replaces', () => {
    // The fallback in caseOptionLabel reads it, so losing it would strand every case
    // imported before the migration.
    expect(MANAGED_LISTS.map((list) => list.legacyAttribute)).toEqual([
      'al_productsolutiontype',
      'al_samplesource',
      'al_casetype',
      'al_preorpostcheck',
    ]);
  });

  it('resolves a list by key and by value', () => {
    expect(listByKey('product-solution-type')?.value).toBe(LIST_PRODUCT_SOLUTION_TYPE);
    expect(listByValue(LIST_PRODUCT_SOLUTION_TYPE)?.key).toBe('product-solution-type');
    expect(listByKey('no-such-list')).toBeNull();
    expect(listByValue(999)).toBeNull();
  });

  it('builds the Web API field names for a migrated list, and none for one that is not', () => {
    expect(lookupValueField(PRODUCT)).toBe('_al_producttypeid_value');
    expect(lookupFormattedField(PRODUCT)).toBe(
      '_al_producttypeid_value@OData.Community.Display.V1.FormattedValue',
    );
    const sampleSource = listByValue(LIST_SAMPLE_SOURCE)!;
    expect(lookupValueField(sampleSource)).toBeNull();
    expect(lookupFormattedField(sampleSource)).toBeNull();
  });
});

describe('reading the options of one list', () => {
  it('returns only that list', () => {
    const mixed = [...SEED, raw({ al_listoptionid: '9', al_name: 'Thematic', al_list: LIST_SAMPLE_SOURCE })];
    expect(toOptionRows(mixed, PRODUCT, DAY).map((row) => row.label)).not.toContain('Thematic');
  });

  it('orders by the sort order an administrator set', () => {
    expect(toOptionRows(SEED, PRODUCT, DAY).map((row) => row.label)).toEqual([
      'Accumulation investment',
      'Accumulation Pension',
      'IHT',
      'Protection',
      'No change reviews',
    ]);
  });

  it('appends an option with no sort order rather than putting it first', () => {
    // Zero would be the obvious default and is the wrong one: adding an option without
    // thinking about ordering would jump it above a list somebody had arranged.
    const rows = toOptionRows(
      [...SEED, raw({ al_listoptionid: '6', al_name: 'Drawdown' })],
      PRODUCT,
      DAY,
    );
    expect(rows[rows.length - 1].label).toBe('Drawdown');
  });

  it('breaks a tie on the label, ignoring case', () => {
    const rows = toOptionRows(
      [
        raw({ al_listoptionid: 'b', al_name: 'beta', al_sortorder: 1 }),
        raw({ al_listoptionid: 'a', al_name: 'Alpha', al_sortorder: 1 }),
      ],
      PRODUCT,
      DAY,
    );
    expect(rows.map((row) => row.label)).toEqual(['Alpha', 'beta']);
  });
});

describe('retiring an option', () => {
  const retired = raw({
    al_listoptionid: '6',
    al_name: 'Drawdown',
    al_sortorder: 60,
    al_effectiveto: '2026-09-20',
  });

  it('is shown in the management table, marked as retired', () => {
    // Hidden would mean an administrator who cannot see that "Drawdown" was retired
    // yesterday creates it again - the reason the question library shows retired questions.
    const rows = toOptionRows([...SEED, retired], PRODUCT, DAY);
    expect(rows.find((row) => row.label === 'Drawdown')?.retired).toBe(true);
  });

  it('is not offered by a picker', () => {
    const rows = toOptionRows([...SEED, retired], PRODUCT, DAY);
    expect(offeredOptions(rows).map((row) => row.label)).not.toContain('Drawdown');
    expect(offeredOptions(rows)).toHaveLength(5);
  });

  it('still reads correctly on a case that already holds it', () => {
    // The point of retiring rather than deleting. A closed case keeps saying what it said.
    const rows = toOptionRows([...SEED, retired], PRODUCT, DAY);
    expect(caseOptionLabel('Drawdown', null, rows)).toBe('Drawdown');
  });

  it('takes effect from the day it is retired, not before', () => {
    const before = toOptionRows([...SEED, retired], PRODUCT, new Date('2026-09-19T12:00:00Z'));
    expect(offeredOptions(before).map((row) => row.label)).toContain('Drawdown');
  });

  it('is not yet offered before the day it starts', () => {
    const future = raw({
      al_listoptionid: '7',
      al_name: 'Bulk transfer',
      al_effectivefrom: '2026-10-01',
    });
    const rows = toOptionRows([...SEED, future], PRODUCT, DAY);
    expect(offeredOptions(rows).map((row) => row.label)).not.toContain('Bulk transfer');
  });
});

describe('what a draft is refused for', () => {
  const rows = toOptionRows(SEED, PRODUCT, DAY);

  function draft(over: Partial<Parameters<typeof validateDraft>[0]> = {}) {
    return { label: 'Drawdown', sortOrder: '', effectiveFrom: null, effectiveTo: null, ...over };
  }

  it('accepts a new option', () => {
    expect(validateDraft(draft(), rows)).toBeNull();
  });

  it('refuses one with no name', () => {
    expect(validateDraft(draft({ label: '   ' }), rows)).toBe('Give the option a name.');
  });

  it('refuses a duplicate of an option already offered, whatever the casing', () => {
    // Two options reading the same thing make a case ambiguous to anybody looking at it,
    // and give the import no way to resolve the extract's text to one row.
    expect(validateDraft(draft({ label: 'iht' }), rows)).toBe('"iht" is already an option on this list.');
  });

  it('allows a name a retired option used to hold', () => {
    // Retiring and re-adding is how a mistake is corrected. Blocking it would mean a typo
    // permanently reserved the name it got wrong.
    const withRetired = toOptionRows(
      [...SEED, raw({ al_listoptionid: '6', al_name: 'Drawdown', al_effectiveto: '2026-09-20' })],
      PRODUCT,
      DAY,
    );
    expect(validateDraft(draft({ label: 'Drawdown' }), withRetired)).toBeNull();
  });

  it('does not treat an option as a duplicate of itself when renaming it', () => {
    expect(validateDraft(draft({ id: '3', label: 'IHT' }), rows)).toBeNull();
  });

  it('refuses a sort order that is not a whole number', () => {
    expect(validateDraft(draft({ sortOrder: '1.5' }), rows)).toBe(
      'Sort order must be a whole number, or left blank.',
    );
    expect(validateDraft(draft({ sortOrder: '-1' }), rows)).not.toBeNull();
    expect(validateDraft(draft({ sortOrder: '60' }), rows)).toBeNull();
  });

  it('refuses a window that closes before it opens', () => {
    expect(
      validateDraft(draft({ effectiveFrom: '2026-10-01', effectiveTo: '2026-09-01' }), rows),
    ).toBe('The date an option stops being offered must be after the date it starts.');
  });
});

describe('a case that has not been backfilled yet', () => {
  const rows = toOptionRows(SEED, PRODUCT, DAY);

  it('reads through the choice value it was imported with', () => {
    // THE migration safety net. Every case written before the lookup existed carries the
    // integer and no lookup, and must keep reading correctly on every page until it is
    // backfilled - not show a blank where the product type used to be.
    expect(caseOptionLabel(null, 120910521, rows)).toBe('Accumulation Pension');
  });

  it('follows a rename, because it resolves through the row', () => {
    // Matched on legacyValue rather than a second hard-coded map, so an administrator who
    // renames an option renames it on the old cases too instead of splitting the history.
    const renamed = toOptionRows(
      SEED.map((row) =>
        row.al_listoptionid === '2' ? { ...row, al_name: 'Pension accumulation' } : row,
      ),
      PRODUCT,
      DAY,
    );
    expect(caseOptionLabel(null, 120910521, renamed)).toBe('Pension accumulation');
  });

  it('prefers the lookup once it has one', () => {
    // A backfilled case must not be re-read through a stale integer, or correcting a case's
    // product type would appear not to have worked.
    expect(caseOptionLabel('IHT', 120910521, rows)).toBe('IHT');
  });

  it('reads as empty when it holds neither', () => {
    expect(caseOptionLabel(null, null, rows)).toBeNull();
    expect(caseOptionLabel('   ', undefined, rows)).toBeNull();
  });

  it('reads as empty for a choice value no option claims, rather than inventing one', () => {
    // An integer from a deleted option is orphaned data. Saying nothing is honest; guessing
    // the nearest row would put a wrong product type on a case nobody would think to check.
    expect(caseOptionLabel(null, 120910999, rows)).toBeNull();
  });
});
