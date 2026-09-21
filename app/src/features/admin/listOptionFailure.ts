/**
 * Why a list option could not be saved or deleted, in a sentence that names the reason.
 *
 * The one refusal that matters here is the Restrict cascade: an option a case is using
 * cannot be deleted, and Dataverse is what refuses it. An administrator told only "that
 * option could not be deleted" would try again, conclude the page is broken, and ask for the
 * option to be removed some other way - when what they actually want is to retire it, which
 * the page offers and which does exactly what they mean.
 *
 * None of this is a security boundary, and none of it is the guarantee. The guarantee is
 * CascadeType.Restrict on al_listoption_al_outcomecase_producttype, which holds against any
 * caller by any route. This only reads the refusal back in words.
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
 * The Restrict cascade, refused.
 *
 * Matched on the error code first, because the sentence Dataverse returns is not ours to
 * rely on: DEV answered this with 0x80040227 and a message naming the relationship and the
 * blocking case, and the phrasing is the part most likely to change under us.
 */
function isInUse(text: string): boolean {
  return (
    text.includes('0x80040227') ||
    /cascade restrict relation/i.test(text) ||
    /associated with another object and cannot be deleted/i.test(text)
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

export const SAVE_FALLBACK = 'That option could not be saved.';
export const DELETE_FALLBACK = 'That option could not be deleted.';

export const OPTION_IN_USE =
  'Cases are already using this option, so it cannot be deleted. Retire it instead: it ' +
  'stops being offered on new cases, and the cases that already have it keep reading ' +
  'correctly.';

export const UNREACHABLE =
  'The connection to Dataverse could not be reached, so nothing was changed. Check your ' +
  'network and try again.';

/** Whether the browser believes it is online, tolerating an environment without navigator. */
function browserOnline(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false;
}

export function describeSaveFailure(error: unknown, online = browserOnline()): string {
  const text = textOf(error);
  if (isUnreachable(text, online)) return UNREACHABLE;
  return SAVE_FALLBACK;
}

export function describeDeleteFailure(error: unknown, online = browserOnline()): string {
  const text = textOf(error);
  // In use before unreachable: a Restrict refusal is a real answer from Dataverse, and
  // reporting it as a network problem would send the administrator to check their wifi.
  if (isInUse(text)) return OPTION_IN_USE;
  if (isUnreachable(text, online)) return UNREACHABLE;
  return DELETE_FALLBACK;
}
