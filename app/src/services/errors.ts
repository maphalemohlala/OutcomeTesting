import type { CommandFailure, CommandFailureKind } from './commands/failures';

/**
 * Single place that turns a classified failure into user-facing copy and keeps the
 * technical detail out of the UI. Support-facing detail is logged, never rendered
 * (NFR-OBS-01): the user sees a meaningful sentence, the console/telemetry keeps the raw cause.
 */

/** Friendly default per failure kind, used when the server sends no specific message. */
export const DEFAULT_FAILURE_MESSAGES: Record<CommandFailureKind, string> = {
  validation: 'Please check the highlighted details and try again.',
  unauthorized:
    'You do not have permission to perform this action. Please contact your administrator if you believe this is incorrect.',
  notFound:
    'The selected record could not be found. It may have been removed, or you may not have access to it.',
  conflict: 'The record has been modified by another user. Please refresh and try again.',
  precondition: 'This action cannot be performed while the record is in its current state.',
  unexpected:
    'The action could not be completed because of a problem on the server. Please try again, and report it if it happens again.',
  // Says nothing about what was saved: it can follow a step that did save, and a request the
  // browser lost may still have landed. An expired sign-in fails the same way as a dropped
  // network, so it names both.
  unavailable:
    'The app could not reach the server. Check your connection, reload the app and sign in again if asked, then check whether the change was made before trying again.',
};

/**
 * Internals a server sentence must not carry to the screen: a plug-in class, an exception
 * type, a platform fault, an id or a table name.
 */
const TECHNICAL =
  /OutcomeTesting\.Plugins\.|Exception|OrganizationServiceFault|With Id =|\bal_\w+|[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/i;

/** The plain form the server has written unexpected faults in since 2026-11-17. */
const FRIENDLY_UNEXPECTED = / could not be completed because /;

/**
 * The message to show the user for a failed command. Server-specific text is preferred:
 * a rule speaking deliberately (validation, permission, not-found, conflict,
 * precondition), and an unexpected fault in the server's plain form - what failed and why.
 *
 * An unexpected fault that still looks technical (an older server, or a message that
 * escaped the plain form) keeps the friendly sentence, and so does a system-level failure,
 * whose text is infrastructure detail that has carried a host and an internal address.
 * classify logs the raw cause either way.
 */
export function messageForFailure(failure: CommandFailure): string {
  if (failure.kind === 'unavailable') return DEFAULT_FAILURE_MESSAGES.unavailable;
  const server = failure.message?.trim();
  if (failure.kind === 'unexpected') {
    return server && FRIENDLY_UNEXPECTED.test(server) && !TECHNICAL.test(server)
      ? server
      : DEFAULT_FAILURE_MESSAGES.unexpected;
  }
  return server && server.length > 0 ? server : DEFAULT_FAILURE_MESSAGES[failure.kind];
}

/** Records technical detail for support without exposing it to the user. */
export function logTechnical(context: string, detail: unknown): void {
  let message: string;
  if (detail instanceof Error) {
    message = detail.stack ?? detail.message;
  } else if (typeof detail === 'string') {
    message = detail;
  } else {
    try {
      message = JSON.stringify(detail);
    } catch {
      message = String(detail);
    }
  }
  console.error(`[OutcomeTesting] ${context}: ${message}`);
}
