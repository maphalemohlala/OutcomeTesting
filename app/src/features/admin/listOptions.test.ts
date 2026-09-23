import { describe, expect, it } from 'vitest';
import {
  RESOURCE_KEYS,
  can,
  pageResourceForPath,
  resolvePermissions,
} from '../../types/permissions';
import {
  LIST_CASE_TYPE,
  LIST_PRE_OR_POST_CHECK,
  LIST_PRODUCTS,
  LIST_PRODUCT_SOLUTION_TYPE,
  LIST_SAMPLE_SOURCE,
  MANAGED_LISTS,
  MIGRATED_LISTS,
  MULTI_CHOICE_LISTS,
  SINGLE_CHOICE_LISTS,
  type RawListOption,
  caseOptionLabel,
  choicesIncludingHeld,
  filterTickOptions,
  heldOptionLabels,
  listByKey,
  listByValue,
  lookupFormattedField,
  lookupLabelOn,
  lookupValueField,
  offeredOptions,
  setValues,
  toggleValue,
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

describe('heldOptionLabels', () => {
  // The four single-choice lists resolve their label off the case row, because a lookup
  // carries its target's name. A set carries nothing, so the header showed the free-text
  // column it replaced and a ticked product never appeared on screen (project owner,
  // 2026-09-21: "the product page is still a free text").
  const PRODUCTS: RawListOption[] = [
    raw({ al_listoptionid: 'p1', al_name: 'Pension', al_list: LIST_PRODUCTS, al_sortorder: 10 }),
    raw({ al_listoptionid: 'p2', al_name: 'ISA', al_list: LIST_PRODUCTS, al_sortorder: 20 }),
    raw({
      al_listoptionid: 'p3',
      al_name: 'Endowment',
      al_list: LIST_PRODUCTS,
      al_sortorder: 30,
      al_effectiveto: '2026-01-01',
    }),
  ];
  const products = listByKey('products')!;

  it('names every product the case covers', () => {
    expect(heldOptionLabels(PRODUCTS, products, ['p1', 'p2'], DAY)).toEqual(['Pension', 'ISA']);
  });

  it('reads in the list order, not the order the intersect came back in', () => {
    // Two cases covering the same products must read identically; the intersect has no
    // meaningful order of its own.
    expect(heldOptionLabels(PRODUCTS, products, ['p2', 'p1'], DAY)).toEqual(['Pension', 'ISA']);
  });

  it('still names a product that has since been retired', () => {
    // A case that covers a withdrawn product still covers it. Dropping it would understate
    // what was checked, which is the same reason choicesIncludingHeld keeps one offered.
    expect(heldOptionLabels(PRODUCTS, products, ['p3'], DAY)).toEqual(['Endowment']);
  });

  it('holds nothing when the case covers nothing', () => {
    expect(heldOptionLabels(PRODUCTS, products, [], DAY)).toEqual([]);
    expect(heldOptionLabels(PRODUCTS, products, ['', '  '], DAY)).toEqual([]);
  });

  it('skips an id the catalogue does not know rather than printing a guid', () => {
    expect(heldOptionLabels(PRODUCTS, products, ['p1', 'gone'], DAY)).toEqual(['Pension']);
  });

  it('ignores an option that belongs to another list', () => {
    // All five lists share one table, so without the filter a sample source attached by
    // hand would read as a product.
    expect(heldOptionLabels([...PRODUCTS, ...SEED], products, ['p1', '3'], DAY)).toEqual([
      'Pension',
    ]);
  });

  it('matches ids whatever case the Web API returned them in', () => {
    // The intersect returns guids lower-cased; a FetchXML read does not.
    expect(heldOptionLabels(PRODUCTS, products, ['P1'], DAY)).toEqual(['Pension']);
  });
});

describe('the managed lists', () => {
  it('names the four the original request named, plus Products', () => {
    // The first four are the ones the owner listed on 2026-09-21. Products was added after,
    // on the same day, when the free-text "Product(s)" field became a dropdown too.
    expect(MANAGED_LISTS.map((list) => list.label)).toEqual([
      'Product / solution type',
      'Sample source',
      'Case type',
      'Pre or post check',
      'Products',
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

  it('offers every list, because each has somewhere to put a chosen option', () => {
    // Product / solution type went first as a pilot; the other three followed once the shape
    // held, and Products after that.
    expect(MIGRATED_LISTS.map((list) => list.value)).toEqual([
      LIST_PRODUCT_SOLUTION_TYPE,
      LIST_SAMPLE_SOURCE,
      LIST_CASE_TYPE,
      LIST_PRE_OR_POST_CHECK,
      LIST_PRODUCTS,
    ]);

    // A lookup OR a many-to-many. Products has the latter, and is manageable on exactly the
    // same page as the rest - which is the whole point of holding one table of options.
    for (const list of MANAGED_LISTS) {
      expect(
        list.caseAttribute !== null || list.caseRelationship !== null,
        list.key,
      ).toBe(true);
    }
  });

  it('holds Products through a relationship, and the others through a lookup', () => {
    // A case covers several products - the column it replaces is labelled "Product(s)" and
    // the seeded fixture is "Pension; ISA" - so a single lookup would make the field able to
    // record less than the free text it replaces.
    expect(MULTI_CHOICE_LISTS.map((l) => l.key)).toEqual(['products']);
    expect(SINGLE_CHOICE_LISTS.map((l) => l.key)).toEqual([
      'product-solution-type',
      'sample-source',
      'case-type',
      'pre-or-post-check',
    ]);

    // Never both: the two are different storage and a list claiming each would be written
    // twice and read inconsistently.
    for (const list of MANAGED_LISTS) {
      expect(list.caseAttribute === null || list.caseRelationship === null, list.key).toBe(true);
    }
  });

  it('would not offer a list with nowhere to put a chosen option', () => {
    // The guard that mattered while three lists were unmigrated, kept for the next one: an
    // option chosen for a list with no lookup could not be saved, so offering it would be a
    // page that takes an administrator's work and drops it. Asserted against a constructed
    // list rather than a real one, so migrating every list did not quietly retire the rule.
    const unmigrated = { ...MANAGED_LISTS[0], caseAttribute: null };
    expect(MIGRATED_LISTS).not.toContain(unmigrated);
    expect(lookupValueField(unmigrated)).toBeNull();
    expect(lookupFormattedField(unmigrated)).toBeNull();
    expect(lookupLabelOn({ al_producttypeidname: 'IHT' }, unmigrated)).toBeNull();
  });

  it('keeps naming the choice column each list replaces', () => {
    // The fallback in caseOptionLabel reads it, so losing it would strand every case
    // imported before the migration.
    expect(MANAGED_LISTS.map((list) => list.legacyAttribute)).toEqual([
      'al_productsolutiontype',
      'al_samplesource',
      'al_casetype',
      'al_preorpostcheck',
      'al_products',
    ]);
  });

  it('resolves a list by key and by value', () => {
    expect(listByKey('product-solution-type')?.value).toBe(LIST_PRODUCT_SOLUTION_TYPE);
    expect(listByValue(LIST_PRODUCT_SOLUTION_TYPE)?.key).toBe('product-solution-type');
    expect(listByKey('no-such-list')).toBeNull();
    expect(listByValue(999)).toBeNull();
  });

  it('builds the Web API field names for each list', () => {
    expect(lookupValueField(PRODUCT)).toBe('_al_producttypeid_value');
    expect(lookupFormattedField(PRODUCT)).toBe(
      '_al_producttypeid_value@OData.Community.Display.V1.FormattedValue',
    );
    expect(lookupValueField(listByValue(LIST_SAMPLE_SOURCE)!)).toBe('_al_samplesourceid_value');
    expect(lookupValueField(listByValue(LIST_CASE_TYPE)!)).toBe('_al_casetypeid_value');
    expect(lookupValueField(listByValue(LIST_PRE_OR_POST_CHECK)!)).toBe('_al_preorpostcheckid_value');
  });

  it('names a lookup distinctly from the choice column it supersedes', () => {
    // The two have to coexist through the migration, so a lookup that collided with its own
    // legacy column could not have been created at all.
    for (const list of MANAGED_LISTS) {
      expect(list.caseAttribute, list.key).not.toBe(list.legacyAttribute);
    }
    const attributes = MANAGED_LISTS.map((list) => list.caseAttribute);
    expect(new Set(attributes).size).toBe(attributes.length);
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

describe('the dropdown options page permission', () => {
  it('is a known resource key, and the resource for its path', () => {
    expect(RESOURCE_KEYS).toContain('page.admin.lists');
    expect(pageResourceForPath('/admin/lists')).toBe('page.admin.lists');
  });

  it('is granted to the Outcome Testing Manager, who runs the process', () => {
    // The point of the whole change. If only administrators could reach this page, adding a
    // product type would still be a request somebody else has to action, which is what the
    // owner asked to stop - it would just be a faster ticket.
    expect(
      can(resolvePermissions(['AL Portal - Outcome Testing Manager']), 'page.admin.lists', 'Manage'),
    ).toBe(true);
  });

  it('is granted to the administrators who run the configuration', () => {
    for (const role of ['AL Portal - Portal Administrator', 'Administrators']) {
      expect(can(resolvePermissions([role]), 'page.admin.lists', 'Manage')).toBe(true);
    }
  });

  it('is not granted to a checker or an adviser', () => {
    // A checker choosing a product type must not also be able to invent one. The vocabulary
    // of the case header is the checking team's to set, not any individual reviewer's.
    for (const role of ['AL Portal - Tax Reviewer', 'AL Portal - AQS Reviewer', 'AL Portal - Adviser Remediation']) {
      expect(can(resolvePermissions([role]), 'page.admin.lists', 'View'), role).toBe(false);
    }
  });

  it('does not carry any authority over a case with it', () => {
    // The guard against "maintains the dropdowns" quietly becoming "can change outcomes".
    const set = resolvePermissions(['AL Portal - Outcome Testing Manager']);
    expect(can(set, 'page.admin.lists', 'Manage')).toBe(true);
    expect(can(set, 'command.signoff', 'Edit')).toBe(false);
    expect(can(set, 'command.regrade', 'Edit')).toBe(false);
  });
});

describe('what an edit control offers', () => {
  const retired = raw({
    al_listoptionid: '6',
    al_name: 'Drawdown',
    al_sortorder: 60,
    al_effectiveto: '2026-09-20',
  });
  const rows = toOptionRows([...SEED, retired], PRODUCT, DAY);

  it('offers the options in force', () => {
    expect(choicesIncludingHeld(rows, null).map((row) => row.label)).toEqual([
      'Accumulation investment',
      'Accumulation Pension',
      'IHT',
      'Protection',
      'No change reviews',
    ]);
  });

  it('also offers the retired option a case actually holds, marked as retired', () => {
    // Otherwise the case renders as "Not set". The value would survive an untouched save,
    // but somebody looking at a blank dropdown reads it as a field that needs filling in.
    const labels = choicesIncludingHeld(rows, '6').map((row) => row.label);
    expect(labels).toContain('Drawdown (retired)');
    expect(labels).toHaveLength(6);
  });

  it('does not offer a retired option to a case that does not hold it', () => {
    expect(choicesIncludingHeld(rows, '3').map((row) => row.label)).not.toContain(
      'Drawdown (retired)',
    );
  });

  it('does not duplicate an option that is held and still offered', () => {
    expect(choicesIncludingHeld(rows, '3')).toHaveLength(5);
  });

  it('ignores a held id no option matches', () => {
    // An orphan from a deleted option. Nothing honest can be shown for it, and inventing a
    // row would put a name on a case that never had one.
    expect(choicesIncludingHeld(rows, 'gone')).toHaveLength(5);
  });
});

describe('reading a lookup label off a case record', () => {
  const ANNOTATION = '_al_producttypeid_value@OData.Community.Display.V1.FormattedValue';

  it('reads the Web API annotation, which is what this app’s reads return', () => {
    expect(lookupLabelOn({ [ANNOTATION]: 'IHT' }, PRODUCT)).toBe('IHT');
  });

  it('reads the SDK-style name too', () => {
    // Reading only one shape is how every adviser mapping came to show "No manager chosen"
    // for a row that had one (F16). There is no reason to learn that twice.
    expect(lookupLabelOn({ al_producttypeidname: 'Protection' }, PRODUCT)).toBe('Protection');
  });

  it('prefers the annotation when both are present', () => {
    expect(
      lookupLabelOn({ [ANNOTATION]: 'IHT', al_producttypeidname: 'Stale' }, PRODUCT),
    ).toBe('IHT');
  });

  it('is null when the record carries neither, or only blanks', () => {
    expect(lookupLabelOn({}, PRODUCT)).toBeNull();
    expect(lookupLabelOn({ [ANNOTATION]: '   ', al_producttypeidname: '' }, PRODUCT)).toBeNull();
  });

  it('is null for a list that has no lookup', () => {
    expect(
      lookupLabelOn({ [ANNOTATION]: 'IHT' }, { ...PRODUCT, caseAttribute: null }),
    ).toBeNull();
  });
});

describe('a list a case may hold several of', () => {
  it('reads a stored set, ignoring blanks and stray spaces', () => {
    expect(setValues('a,b')).toEqual(['a', 'b']);
    expect(setValues(' a , b ,, ')).toEqual(['a', 'b']);
    expect(setValues('')).toEqual([]);
    expect(setValues(null)).toEqual([]);
  });

  it('adds and removes one option at a time', () => {
    expect(toggleValue(['a'], 'b', true)).toBe('a,b');
    expect(toggleValue(['a', 'b'], 'a', false)).toBe('b');
    expect(toggleValue([], 'a', false)).toBe('');
  });

  it('gives the same string whatever order the boxes were ticked in', () => {
    // THE reason it sorts. Without it, ticking A then B and B then A produce different
    // strings, the change detection fires on a set nobody changed, and the command is asked
    // to rewrite associations that already match - a write, an audit line, and a reviewer
    // wondering what moved.
    expect(toggleValue(toggleValue([], 'b', true).split(','), 'a', true)).toBe(
      toggleValue(toggleValue([], 'a', true).split(','), 'b', true),
    );
  });

  it('cannot hold the same option twice', () => {
    expect(toggleValue(['a'], 'a', true)).toBe('a');
  });

  it('round-trips through the stored form', () => {
    const stored = toggleValue(['c', 'a'], 'b', true);
    expect(stored).toBe('a,b,c');
    expect(setValues(stored)).toEqual(['a', 'b', 'c']);
  });
});

describe('filtering a long tick list', () => {
  const row = (id: string, label: string) => ({
    id,
    label,
    list: LIST_PRODUCTS,
    sortOrder: null,
    legacyValue: null,
    effectiveFrom: null,
    effectiveTo: null,
    retired: false,
  });

  const products = [
    row('a', 'AIM ISA'),
    row('b', 'Offshore Bond'),
    row('c', 'Existing Offshore Bond - Fund Switch'),
    row('d', 'Whole of Life'),
  ];

  it('keeps a TICKED option even when it does not match the search', () => {
    /*
     * The one rule here that is not obvious and would never be noticed if it broke. The
     * filter is for finding the next option, not for deciding what is selected; hiding a
     * tick behind a search term is how somebody unticks one by accident and never sees it
     * go. On Products - 55 options from 2026-09-23, several of them held at once - the
     * tick they lost is not recoverable by looking at the screen.
     */
    const shown = filterTickOptions(products, ['d'], 'bond');

    expect(shown.map((o) => o.id)).toEqual(['b', 'c', 'd']);
  });

  it('matches anywhere in the label, not just the start', () => {
    // "Existing Offshore Bond - Fund Switch" is found by typing "offshore". Products are
    // named by what they are, with the qualifier first, so a prefix match finds almost
    // nothing a checker actually types.
    expect(filterTickOptions(products, [], 'offshore').map((o) => o.id)).toEqual(['b', 'c']);
  });

  it('ignores case, and treats a blank or spaces as no filter at all', () => {
    expect(filterTickOptions(products, [], 'AIM').map((o) => o.id)).toEqual(['a']);
    expect(filterTickOptions(products, [], 'aim').map((o) => o.id)).toEqual(['a']);
    expect(filterTickOptions(products, [], '')).toHaveLength(4);
    expect(filterTickOptions(products, [], '   ')).toHaveLength(4);
  });

  it('returns a copy, so the caller cannot sort the catalogue by accident', () => {
    const shown = filterTickOptions(products, [], '');
    expect(shown).not.toBe(products);
  });
});
