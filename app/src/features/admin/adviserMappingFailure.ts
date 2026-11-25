import {
  UNREACHABLE,
  browserOnline,
  isUnreachable,
  serverReason,
  textOf,
} from '../../services/commands/failures';
import type { AdviserMappingRow, ContactOption } from './adviserMappingRows';

/**
 * Why a mapping could not be saved or removed, in a sentence that names the reason.
 *
 * Every refusal on this page used to read "That mapping could not be saved." — a duplicate
 * adviser and a dropped connection alike (F17, 2026-09-20). The behaviour underneath was
 * right both times, and nothing from the fault leaked, but the administrator was told
 * nothing they could act on.
 *
 * None of this is a security boundary. An adviser may have several T&C Managers but never the
 * same one twice: a Dataverse alternate key on `al_adviseremail` + `al_tcmanagerid` is the
 * gate, and this only reads the refusal back.
 */

/**
 * The alternate key on adviser and manager, refused. Dataverse says so three ways and the
 * wording of the sentence is not ours to rely on, so the error code and the structured
 * duplicate-attribute detail are checked too.
 */
function isDuplicateAdviser(text: string): boolean {
  return (
    text.includes('DuplicateAttributes') ||
    text.includes('0x80060892') ||
    /Entity Key .*violated/i.test(text) ||
    /duplicate record cannot be created/i.test(text)
  );
}

export const SAVE_FALLBACK = 'That mapping could not be saved.';
export const REMOVE_FALLBACK = 'That mapping could not be removed.';

export const DUPLICATE_ADVISER =
  'That adviser already has this T&C Manager. Choose a different manager to add.';

export function describeSaveFailure(error: unknown, online = browserOnline()): string {
  const text = textOf(error);
  if (isDuplicateAdviser(text)) return DUPLICATE_ADVISER;
  // The server's reason before the connection: a reason proves the request arrived.
  return serverReason(error) ?? (isUnreachable(text, online) ? UNREACHABLE : SAVE_FALLBACK);
}

export function describeRemoveFailure(error: unknown, online = browserOnline()): string {
  return serverReason(error) ?? (isUnreachable(textOf(error), online) ? UNREACHABLE : REMOVE_FALLBACK);
}

/**
 * The refusal for giving an adviser a T&C Manager the page can already see them holding, or
 * null. Another manager for the same adviser is fine: an adviser may have several.
 *
 * Checked before the round trip purely so the answer is immediate — the alternate key is
 * what actually prevents it, and it prevents it however the row is created. `exceptId` is
 * the row being changed, which always matches itself.
 */
export function alreadyMappedRefusal(
  adviserEmail: string,
  managerId: string,
  mappings: readonly AdviserMappingRow[],
  exceptId: string | null = null,
): string | null {
  const email = adviserEmail.trim().toLowerCase();
  const manager = managerId.trim().toLowerCase();
  if (email === '' || manager === '') return null;
  const clash = mappings.some(
    (m) =>
      m.id !== exceptId &&
      m.adviserEmail.trim().toLowerCase() === email &&
      (m.managerId ?? '').trim().toLowerCase() === manager,
  );
  return clash ? DUPLICATE_ADVISER : null;
}

export const SELF_MAPPING =
  'An adviser cannot be their own T&C Manager. Choose someone else to sign off their cases.';

/**
 * The refusal for naming the adviser as their own T&C Manager, or null. People are matched
 * by work email, so this compares the adviser's email with the chosen manager's.
 */
export function selfMappingRefusal(
  adviserEmail: string,
  managerId: string,
  people: readonly ContactOption[],
): string | null {
  const email = adviserEmail.trim().toLowerCase();
  const id = managerId.trim().toLowerCase();
  if (email === '' || id === '') return null;
  const manager = people.find((p) => p.id.toLowerCase() === id);
  return manager && manager.email.trim().toLowerCase() === email ? SELF_MAPPING : null;
}
