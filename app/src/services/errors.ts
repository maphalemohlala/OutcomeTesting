import type { CommandFailure, CommandFailureKind } from './commands/commandClient';

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
  unavailable: 'Something went wrong while processing your request. Please try again later.',
};

/**
 * The message to show the user for a failed command. Server-specific text is preferred
 * for the kinds that carry a meaningful reason (validation, permission, not-found,
 * conflict, precondition); a system-level failure never leaks raw detail.
 */
export function messageForFailure(failure: CommandFailure): string {
  if (failure.kind === 'unavailable') {
    return DEFAULT_FAILURE_MESSAGES.unavailable;
  }
  const server = failure.message?.trim();
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
