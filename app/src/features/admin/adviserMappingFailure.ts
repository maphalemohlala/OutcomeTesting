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
 * None of this is a security boundary. The uniqueness of an adviser is a Dataverse
 * alternate key on `al_adviseremail` and stays the gate; this only reads the refusal back.
 */

/**
 * The alternate key on al_adviseremail, refused. Dataverse says so three ways and the
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
  'That adviser is already mapped. Use Change on their row to point them at a different ' +
  'T&C Manager.';

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
 * The refusal for mapping an adviser the page can already see mapped, or null.
 *
 * Checked before the round trip purely so the answer is immediate — the alternate key is
 * what actually prevents it, and it prevents it however the row is created.
 */
export function alreadyMappedRefusal(
  adviserEmail: string,
  mappings: readonly AdviserMappingRow[],
): string | null {
  const email = adviserEmail.trim().toLowerCase();
  if (email === '') return null;
  const clash = mappings.some((m) => m.adviserEmail.trim().toLowerCase() === email);
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
