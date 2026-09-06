import { DEFAULT_FAILURE_MESSAGES, logTechnical } from '../errors';

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
const FAILURE_PREFIXES: Record<string, CommandFailureKind> = {
  'VALIDATION:': 'validation',
  'UNAUTHORIZED:': 'unauthorized',
  'NOTFOUND:': 'notFound',
  'CONFLICT:': 'conflict',
  'PRECONDITION:': 'precondition',
};

export type CommandFailureKind =
  | 'validation'
  | 'unauthorized'
  | 'notFound'
  | 'conflict'
  | 'precondition'
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
 * Everything from the first quote, backslash or newline onward is machine detail, so the
 * sentence ends there. Both spellings occur: a raw body carries a bare `"`, a stringified
 * one carries `\"`.
 */
export function sentenceAfterPrefix(message: string, prefix: string): string {
  const rest = message.slice(message.indexOf(prefix) + prefix.length);
  const boundary = rest.search(/["\\\r\n]/);
  return (boundary === -1 ? rest : rest.slice(0, boundary)).trim();
}

/** Classifies a rejected command into the kind and the sentence to show. */
export function classify(error: unknown): CommandFailure {
  const message = extractErrorMessage(error);
  for (const prefix of Object.keys(FAILURE_PREFIXES)) {
    if (message.includes(prefix)) {
      // An empty sentence falls through to the friendly default in messageForFailure,
      // rather than back to the raw body — falling back to the body is what leaked the
      // diagnostics in the first place.
      return { ok: false, kind: FAILURE_PREFIXES[prefix], message: sentenceAfterPrefix(message, prefix) };
    }
  }

  // Unclassified failures are system-level: log the raw cause for support, show a safe message.
  logTechnical('command failed', error);
  return {
    ok: false,
    kind: 'unavailable',
    message: DEFAULT_FAILURE_MESSAGES.unavailable,
  };
}
