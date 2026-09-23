import { inForceOn } from '../../lib/effectiveWindow';

/**
 * The case-header lists whose options are stored as ROWS rather than as picklist metadata,
 * so the people who run the checking process can add, rename and retire them without a
 * developer (project owner, 2026-09-21: "for dropdown fields like Product / solution type,
 * Sample source, Case type, and Pre or post check - How easy would it be to create a
 * management page where users can add, edit, and remove options?").
 *
 * Why rows, and not a management page over the choice columns themselves. Three reasons, and
 * each one is on its own sufficient:
 *
 *  1. Adding an option to a choice column is a METADATA write - InsertOptionValueRequest -
 *     which needs the System Customizer privilege. That is not a privilege an end user holds,
 *     and giving it to them to reach one dropdown hands them the whole schema.
 *  2. It is an authoring act, and TEST and PROD run the managed solution. A metadata edit made
 *     there is an unmanaged customisation layered on managed: it never flows back to DEV, and
 *     the environments diverge silently. The standing rule is that DEV is the only authoring
 *     environment, and a self-service page that can only be used in DEV is not self-service.
 *  3. The PICKERS are hard-coded. CaseEditPanel builds its dropdown from the generated
 *     constant `Al_outcomecasesal_productsolutiontype`, and OT Review Detail writes all five
 *     <option> tags into Liquid by hand. A new option added in Dataverse would render fine on
 *     a case that already held it - `choiceLabel` prefers the server's formatted name - but
 *     nobody could CHOOSE it until the app was rebuilt and the template re-pushed.
 *
 * Rows have none of those problems: they travel as ordinary data, need no privilege beyond
 * the table's own permissions, and need no rebuild, because a picker that reads rows shows
 * whatever rows exist.
 *
 * One table for all four lists, keyed by `al_list`, rather than a table each. Adding a new
 * LIST stays a developer change - it is a schema decision, and it needs a lookup column on
 * the case - but adding an OPTION to an existing list is data. That is the split the request
 * actually asks for, and it is what makes the three lists after the first one nearly free.
 */

/** al_listoption.al_list. A fresh block: 120910814 is the highest value allocated elsewhere. */
export const LIST_PRODUCT_SOLUTION_TYPE = 120910840 as const;
export const LIST_SAMPLE_SOURCE = 120910841 as const;
export const LIST_CASE_TYPE = 120910842 as const;
export const LIST_PRE_OR_POST_CHECK = 120910843 as const;
export const LIST_PRODUCTS = 120910844 as const;

/**
 * The four values, as literals rather than `number`.
 *
 * Not fussiness: the generated model types `al_listoption.al_list` as exactly this union, so
 * a write from useListOptions only compiles while the two agree. Adding a list here and
 * forgetting to add it to the choice column in Dataverse - or the reverse - fails the build
 * instead of failing at runtime on somebody's save. It is the same trick trailLight.ts uses
 * to pin a generated column, and it costs nothing.
 */
export type ListValue =
  | typeof LIST_PRODUCT_SOLUTION_TYPE
  | typeof LIST_SAMPLE_SOURCE
  | typeof LIST_CASE_TYPE
  | typeof LIST_PRE_OR_POST_CHECK
  | typeof LIST_PRODUCTS;

export interface ManagedList {
  /** al_listoption.al_list */
  readonly value: ListValue;
  /** Stable key for routing and tests; never shown to a person. */
  readonly key: string;
  /** What a person calls the list. */
  readonly label: string;
  /**
   * The lookup on al_outcomecase that holds the chosen option, or null for a list that is
   * not held by a lookup - either because it is not migrated, or because a case can hold
   * SEVERAL of its options and it is attached through `caseRelationship` instead.
   */
  readonly caseAttribute: string | null;
  /**
   * The many-to-many that attaches this list's options to a case, for a list where a case
   * may hold more than one.
   *
   * Products is the only one. The column it replaces is labelled "Product(s)" and the seeded
   * fixture is "Pension; ISA", so a single lookup would let the field record less than the
   * free text it replaces - which is not an upgrade.
   */
  readonly caseRelationship: string | null;
  /**
   * The choice column this list replaces.
   *
   * Kept after migration, deliberately. A case imported before the change carries the integer
   * and no lookup, and it has to keep reading correctly - see `caseOptionLabel`. Dropping the
   * column is a later, separate step once nothing is left holding only the integer.
   */
  readonly legacyAttribute: string;
}

export const MANAGED_LISTS: readonly ManagedList[] = [
  {
    value: LIST_PRODUCT_SOLUTION_TYPE,
    key: 'product-solution-type',
    caseRelationship: null,
    label: 'Product / solution type',
    caseAttribute: 'al_producttypeid',
    legacyAttribute: 'al_productsolutiontype',
  },
  {
    value: LIST_SAMPLE_SOURCE,
    key: 'sample-source',
    caseRelationship: null,
    label: 'Sample source',
    caseAttribute: 'al_samplesourceid',
    legacyAttribute: 'al_samplesource',
  },
  {
    value: LIST_CASE_TYPE,
    key: 'case-type',
    caseRelationship: null,
    label: 'Case type',
    caseAttribute: 'al_casetypeid',
    legacyAttribute: 'al_casetype',
  },
  {
    value: LIST_PRE_OR_POST_CHECK,
    key: 'pre-or-post-check',
    caseRelationship: null,
    label: 'Pre or post check',
    caseAttribute: 'al_preorpostcheckid',
    legacyAttribute: 'al_preorpostcheck',
  },
  {
    value: LIST_PRODUCTS,
    key: 'products',
    // Held through a many-to-many, not a lookup: a case covers several products.
    caseRelationship: 'al_listoption_al_outcomecase_products',
    label: 'Products',
    caseAttribute: null,
    legacyAttribute: 'al_products',
  },
];

/**
 * The lists the management page offers: the ones a chosen option has somewhere to go.
 *
 * Either a lookup or a many-to-many counts. A list with NEITHER is declared but not connected
 * to anything - its options could be maintained but never used - and offering it would be a
 * page that takes an administrator's work and drops it.
 */
export const MIGRATED_LISTS: readonly ManagedList[] = MANAGED_LISTS.filter(
  (list) => list.caseAttribute !== null || list.caseRelationship !== null,
);

/** The lists a case holds ONE of: the ones drawn as a single-choice dropdown. */
export const SINGLE_CHOICE_LISTS: readonly ManagedList[] = MANAGED_LISTS.filter(
  (list) => list.caseAttribute !== null,
);

/** The lists a case may hold SEVERAL of. */
export const MULTI_CHOICE_LISTS: readonly ManagedList[] = MANAGED_LISTS.filter(
  (list) => list.caseRelationship !== null,
);

export function listByKey(key: string): ManagedList | null {
  return MANAGED_LISTS.find((list) => list.key === key) ?? null;
}

export function listByValue(value: number): ManagedList | null {
  return MANAGED_LISTS.find((list) => list.value === value) ?? null;
}

/** The Web API field carrying a case's chosen option id, e.g. `_al_producttypeid_value`. */
export function lookupValueField(list: ManagedList): string | null {
  return list.caseAttribute === null ? null : `_${list.caseAttribute}_value`;
}

/** The Web API annotation carrying that lookup's label. */
export function lookupFormattedField(list: ManagedList): string | null {
  const field = lookupValueField(list);
  return field === null ? null : `${field}@OData.Community.Display.V1.FormattedValue`;
}

/** The shape the Web API returns for an al_listoption row. */
export interface RawListOption {
  al_listoptionid: string;
  al_name?: string | null;
  al_list?: number | null;
  al_sortorder?: number | null;
  al_legacyvalue?: number | null;
  al_effectivefrom?: string | null;
  al_effectiveto?: string | null;
}

export interface ListOptionRow {
  id: string;
  label: string;
  list: number;
  sortOrder: number | null;
  /**
   * The choice value this option replaced, or null for an option added since. It is what
   * lets a case that still holds only the integer resolve to this row's label.
   */
  legacyValue: number | null;
  effectiveFrom: string | null;
  effectiveTo: string | null;
  /** Out of force on the day asked about: shown in the management table, never offered. */
  retired: boolean;
}

/**
 * Order: the sort order an administrator set, then the label.
 *
 * An option with no sort order sorts after every option that has one rather than at zero, so
 * adding an option without thinking about ordering appends it instead of silently jumping it
 * to the top of a list somebody has arranged.
 */
function byOrderThenLabel(a: ListOptionRow, b: ListOptionRow): number {
  const left = a.sortOrder ?? Number.MAX_SAFE_INTEGER;
  const right = b.sortOrder ?? Number.MAX_SAFE_INTEGER;
  if (left !== right) return left - right;
  return a.label.localeCompare(b.label, 'en', { sensitivity: 'base' });
}

/**
 * Every option in one list, retired ones included and flagged.
 *
 * The management table shows retired options rather than hiding them, for the reason the
 * question library shows retired questions: an administrator who cannot see that "IHT" was
 * retired last week will create it again.
 */
export function toOptionRows(
  raw: readonly RawListOption[],
  list: ManagedList,
  asOf: Date,
): ListOptionRow[] {
  return raw
    .filter((row) => row.al_list === list.value)
    .map((row) => ({
      id: row.al_listoptionid,
      label: (row.al_name ?? '').trim(),
      list: list.value,
      sortOrder: row.al_sortorder ?? null,
      legacyValue: row.al_legacyvalue ?? null,
      effectiveFrom: row.al_effectivefrom ?? null,
      effectiveTo: row.al_effectiveto ?? null,
      retired: !inForceOn(row.al_effectivefrom, row.al_effectiveto, asOf),
    }))
    .sort(byOrderThenLabel);
}

/** The options a picker offers: in force, in order. */
export function offeredOptions(rows: readonly ListOptionRow[]): ListOptionRow[] {
  return rows.filter((row) => !row.retired);
}

export interface OptionDraft {
  /** Set when editing, absent when adding. */
  id?: string;
  label: string;
  sortOrder: string;
  effectiveFrom: string | null;
  effectiveTo: string | null;
}

/**
 * Why a draft cannot be saved, as a sentence to show the administrator, or null.
 *
 * Rendering only, in the sense NFR-SEC-01 means it: this decides what the page says, never
 * what the platform allows. The table's own permissions decide who may write at all, and the
 * lookup's Restrict cascade is what actually stops an in-use option being deleted.
 */
export function validateDraft(
  draft: OptionDraft,
  siblings: readonly ListOptionRow[],
): string | null {
  const label = draft.label.trim();
  if (label === '') return 'Give the option a name.';

  /*
   * Unique among the options still IN FORCE, not among all of them. A retired "IHT" must not
   * block a new one: retiring and re-adding is how an administrator corrects a mistake, and
   * the two rows mean the same thing to anybody reading a case.
   */
  const clash = siblings.some(
    (row) =>
      row.id !== draft.id &&
      !row.retired &&
      row.label.localeCompare(label, 'en', { sensitivity: 'base' }) === 0,
  );
  if (clash) return `"${label}" is already an option on this list.`;

  const order = draft.sortOrder.trim();
  if (order !== '') {
    if (!/^\d+$/.test(order)) return 'Sort order must be a whole number, or left blank.';
  }

  if (draft.effectiveFrom && draft.effectiveTo && draft.effectiveTo <= draft.effectiveFrom) {
    return 'The date an option stops being offered must be after the date it starts.';
  }

  return null;
}

/**
 * The label to show for a case's chosen option.
 *
 * The lookup first, then the choice value the case was imported with. That fallback is the
 * whole migration safety net: a case written before the lookup existed carries only the
 * integer, and it has to keep reading correctly on every page until it is backfilled. It is
 * matched through `legacyValue` rather than a second hard-coded map, so renaming an option
 * renames it on the old cases too.
 *
 * Returns null rather than a placeholder: the caller decides how an empty field is drawn.
 */
export function caseOptionLabel(
  lookupFormatted: string | null | undefined,
  legacyValue: number | null | undefined,
  rows: readonly ListOptionRow[],
): string | null {
  const labelled = (lookupFormatted ?? '').trim();
  if (labelled !== '') return labelled;

  if (legacyValue == null) return null;

  const match = rows.find((row) => row.legacyValue === legacyValue);
  const label = (match?.label ?? '').trim();
  return label === '' ? null : label;
}

/**
 * The choices an edit control offers for a case that currently holds `heldId`.
 *
 * The options in force, plus - only when the case holds one that is not among them - the
 * retired option it actually holds, marked as retired.
 *
 * Without that addition a case holding a retired option would render as "Not set". Nothing
 * would be lost, because an untouched field is not sent and so the value would survive; but
 * the person editing the case would be looking at a blank where a product type is, and the
 * obvious reading of a blank dropdown is that the field is empty and needs filling in. It is
 * shown, and shown as retired, so what the case says is what the screen says.
 *
 * A retired option is never offered to a case that does not already hold it, and the server
 * refuses one anyway (ListOptionRules.Resolve).
 */
export function choicesIncludingHeld(
  rows: readonly ListOptionRow[],
  heldId: string | null | undefined,
): ListOptionRow[] {
  const offered = offeredOptions(rows);
  const held = (heldId ?? '').trim();
  if (held === '' || offered.some((row) => row.id === held)) return offered;

  const stillHeld = rows.find((row) => row.id === held);
  if (!stillHeld) return offered;

  return [...offered, { ...stillHeld, label: `${stillHeld.label} (retired)` }];
}

/**
 * A long tick list, narrowed by what somebody typed into its filter box.
 *
 * Pure, and exported, because it carries the one rule in the whole control that is not
 * obvious and would never be noticed if it broke: **a ticked option is never filtered away**.
 * The filter is for FINDING the next option, not for deciding what is selected. Hiding a
 * tick behind a search term is how somebody unticks one by accident and never sees it go -
 * and on Products, where a case may hold several out of 55, the tick they lost is not
 * recoverable by looking at the screen.
 *
 * An empty or whitespace term is not a filter, and matching is case-insensitive on a plain
 * substring: these are product names, not a query language.
 */
export function filterTickOptions(
  options: readonly ListOptionRow[],
  chosen: readonly string[],
  term: string,
): ListOptionRow[] {
  const needle = term.trim().toLowerCase();
  if (needle === '') return [...options];

  return options.filter(
    (option) =>
      option.label.toLowerCase().includes(needle) || chosen.includes(option.id),
  );
}

/**
 * The labels a case holds on a MULTI-choice list, in the list's own order.
 *
 * The single-choice lists resolve their label off the case row, because a lookup carries its
 * target's name with it. A set does not: the case row holds nothing at all, the intersect
 * holds ids, and so the names have to be joined here against the catalogue.
 *
 * Retired options are kept. A case that covers a product withdrawn last month still covers
 * it, and a header that silently dropped it would understate what was checked - the same
 * reason choicesIncludingHeld keeps a held option in the dropdown. An id with no matching
 * row is skipped rather than shown as a guid.
 */
export function heldOptionLabels(
  raw: readonly RawListOption[],
  list: ManagedList,
  ids: readonly string[],
  asOf: Date,
): string[] {
  const held = new Set(ids.map((id) => (id ?? '').trim().toLowerCase()).filter((id) => id !== ''));
  if (held.size === 0) return [];

  return toOptionRows(raw, list, asOf)
    .filter((row) => held.has(row.id.toLowerCase()))
    .map((row) => row.label)
    .filter((label) => label !== '');
}

/**
 * The label a record carries for a managed-list lookup, or null.
 *
 * Both shapes are read. The Web API expresses a lookup's label as the annotation
 * `_al_producttypeid_value@OData.Community.Display.V1.FormattedValue`, while a FetchXML or
 * SDK read supplies `al_producttypeidname`. Reading only one of them is how the adviser
 * mapping table came to show "No manager chosen" for every row that had one (F16), and there
 * is no reason to learn that twice.
 */
export function lookupLabelOn(
  record: Record<string, unknown>,
  list: ManagedList,
): string | null {
  if (list.caseAttribute === null) return null;

  const annotation = lookupFormattedField(list);
  const candidates = [
    annotation === null ? undefined : record[annotation],
    record[`${list.caseAttribute}name`],
  ];

  for (const candidate of candidates) {
    if (typeof candidate === 'string') {
      const label = candidate.trim();
      if (label !== '') return label;
    }
  }

  return null;
}

/**
 * A comma-separated option-id list as ids, blanks dropped.
 *
 * The set a case holds travels as one string because the command's Fields payload is a map
 * of strings, and the WHOLE set is sent on every save - a payload of additions could never
 * remove one.
 */
export function setValues(raw: string | null | undefined): string[] {
  return (raw ?? '')
    .split(',')
    .map((id) => id.trim())
    .filter((id) => id !== '');
}

/**
 * The set with one id added or removed, kept SORTED.
 *
 * Sorted so that ticking A then B and ticking B then A produce the same string. Without it
 * the change detection would fire on a set nobody changed, and the command would be asked to
 * rewrite associations that already match - which is a write, an audit line and a reviewer
 * wondering what moved.
 */
export function toggleValue(
  current: readonly string[],
  id: string,
  on: boolean,
): string {
  const next = on ? [...new Set([...current, id])] : current.filter((held) => held !== id);
  return [...next].sort().join(',');
}
