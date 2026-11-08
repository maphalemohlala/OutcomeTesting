/**
 * The File Quality fail points as the question library manages them.
 *
 * Pure shaping and validation only; the reads and writes are in useFailReasons, which cannot be
 * imported under the test runner because ../../generated pulls in the Power Apps client.
 */

/** al_failreason.al_category. The code prefix is what the V8 seed codes already use. */
export const FAIL_REASON_CATEGORIES = [
  { value: 120910400, label: 'AML', prefix: 'AML' },
  { value: 120910401, label: 'Breach', prefix: 'BRE' },
  { value: 120910402, label: 'Record Keeping', prefix: 'REC' },
  { value: 120910403, label: 'Tax check', prefix: 'TAX' },
  { value: 120910404, label: 'Tax file quality', prefix: 'TFQ' },
] as const;

export interface RawFailReason {
  al_failreasonid: string;
  al_name?: string;
  al_failreasoncode?: string;
  al_category?: number;
  al_displayorder?: number;
  statecode?: number;
}

export interface FailReasonRow {
  id: string;
  name: string;
  code: string;
  category: number | null;
  categoryLabel: string;
  order: number;
  retired: boolean;
}

export interface FailReasonDraft {
  id?: string;
  name: string;
  category: number;
  order: string;
}

export function toFailReasonRows(raw: readonly RawFailReason[]): FailReasonRow[] {
  return raw
    .map((r) => ({
      id: r.al_failreasonid,
      name: r.al_name ?? '',
      code: r.al_failreasoncode ?? '',
      category: r.al_category ?? null,
      categoryLabel: FAIL_REASON_CATEGORIES.find((c) => c.value === r.al_category)?.label ?? '',
      order: r.al_displayorder ?? 0,
      retired: r.statecode === 1,
    }))
    .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name));
}

/**
 * The next free code in a category, e.g. FR-REC-12 after FR-REC-11. Retired rows count, since
 * the code is an alternate key and a retired row still holds its code.
 */
export function nextFailReasonCode(rows: readonly FailReasonRow[], category: number): string {
  const prefix = FAIL_REASON_CATEGORIES.find((c) => c.value === category)?.prefix ?? 'GEN';
  const stem = `FR-${prefix}-`;
  const highest = rows
    .filter((row) => row.code.startsWith(stem))
    .map((row) => Number(row.code.slice(stem.length)))
    .filter((n) => Number.isInteger(n))
    .reduce((max, n) => Math.max(max, n), 0);
  return `${stem}${String(highest + 1).padStart(2, '0')}`;
}

/** One past the last row, so a new fail point goes to the foot of the list by default. */
export function nextDisplayOrder(rows: readonly FailReasonRow[]): number {
  return rows.reduce((max, row) => Math.max(max, row.order), 0) + 1;
}

export function validateFailReasonDraft(
  draft: FailReasonDraft,
  rows: readonly FailReasonRow[],
): string | null {
  const name = draft.name.trim();
  if (name === '') return 'Give the fail point its wording.';

  // Unique among the points still offered: retiring and re-adding is how a mistake is put right.
  const clash = rows.some(
    (row) =>
      row.id !== draft.id &&
      !row.retired &&
      row.name.localeCompare(name, 'en', { sensitivity: 'base' }) === 0,
  );
  if (clash) return `"${name}" is already on the list.`;

  if (!/^\d+$/.test(draft.order.trim())) return 'Display order must be a whole number.';

  return null;
}
