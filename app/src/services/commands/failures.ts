import { DEFAULT_FAILURE_MESSAGES, logTechnical, messageForFailure } from '../errors';

/**
 * How a failed command is turned into a kind and a sentence.
 *
 * Kept apart from commandClient because that module imports the Power Apps data client,
 * which cannot be loaded outside the hosted runtime — importing it in a test fails at
 * module resolution. This file holds only the decision, so the rule that decides what a
 * user is told is unit-testable, the same way CaseLifecycle and peopleDirectory keep
 * their rules free of Dataverse types.
 */

// The plug-in prefixes InvalidPluginExecutionException messages so the client can branch.
//
// UNEXPECTED is PluginBase's own, not a rule's: since 2026-09-10 it wraps every exception
// a plug-in did not raise deliberately - since 2026-11-17 as a plain sentence saying what
// failed and why, which messageForFailure shows unless it still looks technical. Before
// that those left the pipeline uncaught and arrived here as an unclassifiable fault, so the
// user was told "something went wrong while processing your request" whatever had happened.
const FAILURE_PREFIXES: Record<string, CommandFailureKind> = {
  'VALIDATION:': 'validation',
  'UNAUTHORIZED:': 'unauthorized',
  'NOTFOUND:': 'notFound',
  'CONFLICT:': 'conflict',
  'PRECONDITION:': 'precondition',
  'UNEXPECTED:': 'unexpected',
};

export type CommandFailureKind =
  | 'validation'
  | 'unauthorized'
  | 'notFound'
  | 'conflict'
  | 'precondition'
  | 'unexpected'
  | 'unavailable';

export interface CommandSuccess<T> {
  ok: true;
  data: T;
}

export interface CommandFailure {
  ok: false;
  kind: CommandFailureKind;
  message: string;
}

export type CommandResult<T> = CommandSuccess<T> | CommandFailure;

/**
 * Pulls a human-readable message out of whatever the platform throws. The Power Apps
 * data client rejects with a plain object (not an Error), whose message can sit at
 * `.message`, `.error.message` or deeper, so a naive `String(error)` yields
 * "[object Object]" and both the classification and the support log lose the cause.
 */
export function extractErrorMessage(error: unknown, depth = 0): string {
  if (error == null) return '';
  if (typeof error === 'string') return error;
  if (error instanceof Error) return error.message;
  if (typeof error === 'object') {
    const obj = error as Record<string, unknown>;
    for (const key of ['message', 'Message', 'description', 'ExceptionMessage']) {
      const value = obj[key];
      if (typeof value === 'string' && value.trim()) return value;
    }
    if (depth < 4) {
      for (const key of ['error', 'innererror', 'InnerError', 'details', 'cause']) {
        if (obj[key] != null && typeof obj[key] === 'object') {
          const nested = extractErrorMessage(obj[key], depth + 1);
          if (nested) return nested;
        }
      }
    }
    try {
      const json = JSON.stringify(error);
      if (json && json !== '{}') return json;
    } catch {
      // Non-serialisable (e.g. circular): fall through to the default string form.
    }
  }
  return String(error);
}

/**
 * The human sentence a plug-in wrote after its prefix.
 *
 * The fault arrives as a JSON body, so the text after the prefix runs straight on into
 * `","@Microsoft.PowerApps.CDS.…` diagnostics. Slicing to the end of the string put that
 * entire block on the screen, which is what a user reported as an unreadable error.
 * The sentence ends where its JSON string does: a quote, bare in a raw body or `\"` in a
 * stringified one, followed by `,"` or `}`; or at a newline. A quote inside the sentence
 * ("Choose \"Adviser\" first") is not followed by either, so the sentence keeps it.
 */
export function sentenceAfterPrefix(message: string, prefix: string): string {
  const rest = message.slice(message.indexOf(prefix) + prefix.length);
  const boundary = rest.search(/\\*"(?=,\\*"|\})|[\r\n]/);
  return (boundary === -1 ? rest : rest.slice(0, boundary)).replace(/\\+"/g, '"').trim();
}

/**
 * The sentence alone, from a fault that may be wrapped in an OData body.
 *
 * `classify` handles the plug-ins that prefix their messages, because it finds the prefix
 * inside the body and `sentenceAfterPrefix` cuts at the first quote. A guard that raises a
 * BARE sentence has no prefix to find, so nothing cut the body and the whole thing reached
 * the screen: plug-in class, table name, step id, plug-in trace with timings, and the
 * initiating user's GUID (F12, DEV, 2026-09-20).
 *
 * The SDK can hand the body over either as the error itself or as the error's `message`
 * string, so both are unwrapped. Where a prefix IS present it is still stripped, so a
 * caller gets one rule for every fault.
 *
 * An unreadable body returns the empty string rather than itself. Every caller has a
 * friendly default; falling back to the body is what put the diagnostics on screen.
 */
export function plainMessage(error: unknown): string {
  let message = extractErrorMessage(error).trim();

  for (let depth = 0; depth < 4 && message.startsWith('{'); depth += 1) {
    let parsed: unknown;
    try {
      parsed = JSON.parse(message);
    } catch {
      return '';
    }
    const inner = extractErrorMessage(parsed).trim();
    if (!inner || inner === message) return '';
    message = inner;
  }

  if (message.startsWith('{')) return '';

  for (const prefix of Object.keys(FAILURE_PREFIXES)) {
    if (message.includes(prefix)) return sentenceAfterPrefix(message, prefix);
  }
  return message;
}

/** Everything in the error, whatever shape it arrived in, as one searchable string. */
export function textOf(error: unknown): string {
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

/** The request never reached Dataverse. */
export function isUnreachable(text: string, online: boolean): boolean {
  if (!online) return true;
  return (
    /Failed to fetch/i.test(text) ||
    /NetworkError/i.test(text) ||
    /Load failed/i.test(text) ||
    /net::ERR/i.test(text) ||
    /ERR_INTERNET_DISCONNECTED/i.test(text)
  );
}

/**
 * A network that is not there, told apart from a refusal.
 *
 * "Could not be removed" after the browser went offline reads as a rule about the record,
 * and the administrator goes looking for what is wrong with it. The same words as a command
 * that could not reach the server, so the two never disagree.
 */
export const UNREACHABLE = DEFAULT_FAILURE_MESSAGES.unavailable;

/** Whether the browser believes it is online, tolerating an environment without navigator. */
export function browserOnline(): boolean {
  return typeof navigator === 'undefined' || navigator.onLine !== false;
}

/** Classifies a rejected command into the kind and the sentence to show. */
export function classify(error: unknown): CommandFailure {
  const message = extractErrorMessage(error);
  for (const prefix of Object.keys(FAILURE_PREFIXES)) {
    if (message.includes(prefix)) {
      // An empty sentence falls through to the friendly default in messageForFailure,
      // rather than back to the raw body — falling back to the body is what leaked the
      // diagnostics in the first place.
      const kind = FAILURE_PREFIXES[prefix];
      if (kind === 'unexpected') {
        // Logged as well as returned. The other kinds are a rule speaking deliberately;
        // this one is a fault, so support wants the raw body as well as the sentence.
        logTechnical('command failed', error);
      }
      return { ok: false, kind, message: sentenceAfterPrefix(message, prefix) };
    }
  }

  // Unclassified: nothing our code raised, so this is the platform, the network or the
  // page rather than a rule speaking. The cause is logged and deliberately not returned:
  // it is free text from infrastructure and has carried a host and an internal address
  // before now, which NFR-OBS-01 keeps out of the browser.
  logTechnical('command failed', error);
  const text = textOf(error);
  // Only a request that never left the browser is a connection problem. Anything else
  // reached the server, and "check your connection" sends the user to the wrong place.
  if (isUnreachable(text, browserOnline())) {
    return { ok: false, kind: 'unavailable', message: DEFAULT_FAILURE_MESSAGES.unavailable };
  }
  if (isPrivilegeFault(text)) {
    return { ok: false, kind: 'unauthorized', message: DEFAULT_FAILURE_MESSAGES.unauthorized };
  }
  // No shown text, so messageForFailure gives the "problem on the server" sentence.
  return { ok: false, kind: 'unexpected', message: '' };
}

/** Dataverse refusing for a missing security privilege, which carries no prefix of ours. */
function isPrivilegeFault(text: string): boolean {
  return /SecLib::|is missing prv\w* privilege|0x80040220|0x80042f09/i.test(text);
}

/**
 * The server's own reason for a failure, worded for the user, or null when it gave none
 * worth showing: no sentence, a connection failure, or an unexpected fault that still looks
 * technical. For pages that otherwise say only what failed, so they can also say why.
 */
export function serverReason(error: unknown): string | null {
  const failure = classify(error);
  if (failure.kind === 'unavailable' || !failure.message.trim()) return null;
  const shown = messageForFailure(failure);
  return failure.kind === 'unexpected' && shown === DEFAULT_FAILURE_MESSAGES.unexpected
    ? null
    : shown;
}
