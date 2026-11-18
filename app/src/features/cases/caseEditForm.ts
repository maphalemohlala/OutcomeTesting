/**
 * The edit case modal's form rules, apart from the component so they can be tested directly.
 */
import { Al_outcomecasesal_casestatus } from '../../generated/models/Al_outcomecasesModel';
import { canTransition, type CaseStatus } from '../../types/domain';
import type { CaseEditValues } from './caseDetailMapping';

type Attr = keyof CaseEditValues;

/**
 * The fields the form holds differently from the snapshot it was opened on (or last rebased
 * onto), as the strings al_UpdateCaseDetails takes. Only these are sent.
 */
export function changedFrom(
  form: CaseEditValues,
  base: CaseEditValues,
  attrs: readonly Attr[],
): Record<string, string> {
  const changed: Record<string, string> = {};
  for (const attr of attrs) {
    const current = form[attr];
    if (current === base[attr]) continue;
    changed[attr] = current == null ? '' : String(current);
  }
  return changed;
}

/**
 * The form moved onto a newer read of the case. A field the user changed keeps their value;
 * every other field takes the newer one. Without this, a re-read after a conflict left the
 * form holding the old values, which then counted as edits and reverted the other person's
 * change on the next save.
 */
export function rebaseForm(
  form: CaseEditValues,
  base: CaseEditValues,
  next: CaseEditValues,
): CaseEditValues {
  const rebased = { ...next };
  for (const attr of Object.keys(next) as Attr[]) {
    if (form[attr] !== base[attr]) (rebased as Record<Attr, unknown>)[attr] = form[attr];
  }
  return rebased;
}

/** The status a form's status value names, or null where it holds none. */
export function statusOf(value: CaseEditValues['al_casestatus']): CaseStatus | null {
  if (value == null) return null;
  return ((Al_outcomecasesal_casestatus as Record<number, string>)[Number(value)] as CaseStatus) ?? null;
}

/**
 * Whether the modal offers allocation. al_AssignCase moves the case to Assigned, so only
 * where the lifecycle allows that move - from the form's status, so choosing Queued in the
 * same save counts.
 */
export function allocationOpenFor(value: CaseEditValues['al_casestatus']): boolean {
  return canTransition(statusOf(value), 'Assigned');
}

/**
 * The line under a checker field. While the role holders are still being read the list is
 * empty because nothing has arrived yet, not because nobody holds the role, so it says so.
 */
export function checkerHelp(
  security: 'loading' | 'unavailable' | 'ready',
  role: string,
): string {
  if (security === 'loading') return `Loading who holds the ${role} role…`;
  if (security === 'unavailable') {
    return 'Who holds the checker roles could not be read, so there is no one to choose from.';
  }
  return `Only people holding the ${role} role are listed.`;
}

/** What the check looked like: when this form opened, or freshly re-read after a field save. */
export interface CheckSnapshot {
  ownerId: string | null;
  rowVersion: string | null;
}

/**
 * The row version to allocate a check against after a field save in the same submit.
 *
 * The save can move the check's version itself (moving the case releases its checks), so the
 * version the form opened with would refuse our own allocation. The check is re-read instead,
 * and a different owner than the form saw means somebody else allocated it meanwhile: that is
 * a conflict, not something to overwrite. A re-read that failed proves nothing, so it refuses.
 */
export function allocationVersionAfterSave(
  opened: CheckSnapshot,
  fresh: CheckSnapshot | null,
): { rowVersion: string | null } | 'conflict' | 'unknown' {
  if (!fresh) return 'unknown';
  if ((fresh.ownerId ?? null) !== (opened.ownerId ?? null)) return 'conflict';
  return { rowVersion: fresh.rowVersion };
}
