import { plainMessage } from '../../services/commands/failures';
import type { TemplateRow } from './useNotificationTemplates';

/**
 * Taking a stored letter back off the table (F24).
 *
 * <p>
 * The page could add a letter and edit one, and never undo either. That left two states an
 * administrator could reach and not leave: a letter of their own, attached to an event and
 * emailing somebody on every occurrence, with no way to stop it; and a letter of the twelve
 * whose saved wording replaced the built-in copy, with no way to get the copy back.
 * </p>
 * <p>
 * Both are one row, and every server-side read of this table already filters to the active
 * row for a code — so the row is the switch. What was missing was only a way to throw it.
 * </p>
 */

/**
 * The row is DELETED rather than deactivated.
 *
 * `al_templatecode` is the table's alternate key and an inactive row keeps it, so a
 * deactivated letter would be invisible on this page (it lists active rows only) while still
 * holding its code against anybody adding that letter again. That is the trap F18 hit on
 * adviser mappings, and it is the same table shape, so it gets the same answer.
 */
export const REMOVE_FALLBACK = 'That letter could not be removed.';

/**
 * A network that is not there, told apart from a refusal.
 *
 * "Could not be removed" after the browser went offline reads as a rule about the letter,
 * and the administrator goes looking for what is wrong with it.
 */
export const UNREACHABLE =
  'The connection to Dataverse could not be reached, so nothing was changed. Check your ' +
  'network and try again.';

export interface RemovalPrompt {
  /** The dialog's heading, and the wording of the button that opens it. */
  title: string;
  /** What is about to happen, naming the letter. */
  lead: string;
  /** What stops, and what does not. */
  consequence: string;
  /** The confirming button. */
  confirmLabel: string;
}

/**
 * What removing this letter would do, or null where there is nothing to remove.
 *
 * A letter of the twelve that nobody has edited has no row: it is already running on the
 * built-in wording, so offering to restore it would be offering to do nothing.
 */
export function removalPrompt(row: TemplateRow): RemovalPrompt | null {
  if (row.id === null) {
    return null;
  }

  if (row.custom) {
    return {
      title: 'Remove this letter',
      lead: `“${row.name}” is a letter of your own. Removing it deletes the wording — it cannot be recovered from this page.`,
      consequence:
        'It stops being sent from the moment it is removed. Nothing else changes: whatever ' +
        'the system already sends at that event carries on unaltered.',
      confirmLabel: 'Remove the letter',
    };
  }

  return {
    title: 'Use the built-in wording',
    lead: `“${row.name}” is one of the letters this system sends. Discarding your wording brings back the copy built into it — your version cannot be recovered from this page.`,
    consequence:
      'The letter carries on being sent, in its original words, to whoever it went to ' +
      'before anybody edited it. Any recipient chosen here goes back to the default too.',
    confirmLabel: 'Use the built-in wording',
  };
}

/**
 * Why the removal did not happen, in a sentence.
 *
 * A server refusal is repeated as the server wrote it. That is worth doing here and was not
 * worth doing for an adviser mapping (F17, which always falls back): this table has a guard
 * plug-in that raises sentences written to be read, and throwing one away would leave an
 * administrator with "could not be removed" and nothing to act on.
 *
 * `plainMessage` is what makes that safe - it unwraps the OData body and returns nothing at
 * all for one it cannot read, which is the F12 rule: never fall back to the raw body.
 */
export function describeRemoveFailure(error: unknown, online = browserOnline()): string {
  if (isUnreachable(textOf(error), online)) {
    return UNREACHABLE;
  }

  const message = plainMessage(error);

  // `extractErrorMessage` ends at `String(error)`, so an object carrying no readable message
  // arrives as the JS default stringification. That is the absence of a sentence, not one.
  if (message === '' || message === '[object Object]') {
    return REMOVE_FALLBACK;
  }

  return message;
}

/** Everything in the error, whatever shape it arrived in, as one searchable string. */
function textOf(error: unknown): string {
  if (error === null || error === undefined) return '';
  if (typeof error === 'string') return error;
  if (error instanceof Error) return `${error.name} ${error.message}`;
  try {
    return JSON.stringify(error) ?? '';
  } catch {
    return String((error as { message?: unknown }).message ?? '');
  }
}

/** The request never reached Dataverse. */
function isUnreachable(text: string, online: boolean): boolean {
  if (!online) return true;
  return /Failed to fetch|NetworkError|Load failed|net::ERR|ERR_INTERNET_DISCONNECTED/i.test(text);
}

function browserOnline(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false;
}
