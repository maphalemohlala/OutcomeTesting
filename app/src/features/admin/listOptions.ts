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
export const LIST_PRODUCT_SOLUTION_TYPE = 120910840;
export const LIST_SAMPLE_SOURCE = 120910841;
export const LIST_CASE_TYPE = 120910842;
export const LIST_PRE_OR_POST_CHECK = 120910843;

export interface ManagedList {
  /** al_listoption.al_list */
  readonly value: number;
  /** Stable key for routing and tests; never shown to a person. */
  readonly key: string;
  /** What a person calls the list. */
  readonly label: string;
  /**
   * The lookup on al_outcomecase that holds the chosen option, or null while the list is
   * still a choice column and has not been migrated.
   *
   * A list with no case attribute is declared but not connected to anything: its options can
   * be seen but there is nowhere to use them, so the page does not offer it. Migrating one is
   * a lookup column, a backfill and this one field.
   */
  readonly caseAttribute: string | null;
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
    label: 'Product / solution type',
    caseAttribute: 'al_producttypeid',
    legacyAttribute: 'al_productsolutiontype',
  },
  // Declared so the destination is visible and the choice column carries every value it will
  // ever need, but not yet migrated: each needs its own lookup, backfill and picker change.
  {
    value: LIST_SAMPLE_SOURCE,
    key: 'sample-source',
    label: 'Sample source',
    caseAttribute: null,
    legacyAttribute: 'al_samplesource',
  },
  {
    value: LIST_CASE_TYPE,
    key: 'case-type',
    label: 'Case type',
    caseAttribute: null,
    legacyAttribute: 'al_casetype',
  },
  {
    value: LIST_PRE_OR_POST_CHECK,
    key: 'pre-or-post-check',
    label: 'Pre or post check',
    caseAttribute: null,
    legacyAttribute: 'al_preorpostcheck',
  },
];

/** The lists the management page offers: the ones a chosen option has somewhere to go. */
export const MIGRATED_LISTS: readonly ManagedList[] = MANAGED_LISTS.filter(
  (list) => list.caseAttribute !== null,
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
