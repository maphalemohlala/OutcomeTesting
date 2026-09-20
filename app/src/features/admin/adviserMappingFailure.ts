import type { AdviserMappingRow } from './adviserMappingRows';

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

/** Everything in the error, whatever shape it arrived in, as one searchable string. */
function textOf(error: unknown): string {
  if (error === null || error === undefined) return '';
  if (typeof error === 'string') return error;
  if (error instanceof Error) return `${error.name} ${error.message}`;
  try {
    return JSON.stringify(error) ?? '';
  } catch {
    // A circular or otherwise unserialisable error still has a message worth reading.
    return String((error as { message?: unknown }).message ?? '');
  }
}

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

/** The request never reached Dataverse. */
function isUnreachable(text: string, online: boolean): boolean {
  if (!online) return true;
  return (
    /Failed to fetch/i.test(text) ||
    /NetworkError/i.test(text) ||
    /Load failed/i.test(text) ||
    /net::ERR/i.test(text) ||
    /ERR_INTERNET_DISCONNECTED/i.test(text)
  );
}

export const SAVE_FALLBACK = 'That mapping could not be saved.';
export const REMOVE_FALLBACK = 'That mapping could not be removed.';

export const DUPLICATE_ADVISER =
  'That adviser is already mapped. Use Change on their row to point them at a different ' +
  'T&C Manager.';

export const UNREACHABLE =
  'The connection to Dataverse could not be reached, so nothing was changed. Check your ' +
  'network and try again.';

/** Whether the browser believes it is online, tolerating an environment without navigator. */
function browserOnline(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false;
}

export function describeSaveFailure(error: unknown, online = browserOnline()): string {
  const text = textOf(error);
  if (isDuplicateAdviser(text)) return DUPLICATE_ADVISER;
  if (isUnreachable(text, online)) return UNREACHABLE;
  return SAVE_FALLBACK;
}

export function describeRemoveFailure(error: unknown, online = browserOnline()): string {
  if (isUnreachable(textOf(error), online)) return UNREACHABLE;
  return REMOVE_FALLBACK;
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
