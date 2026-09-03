import { executeCommand, type CommandResult } from './commandClient';

/**
 * RegradeCase command (AD-003, OD-007, AD-031). The T&C Manager reopens, overrides or
 * regrades a graded outcome by setting the final outcome with a mandatory reason. The
 * Custom API al_RegradeCase preserves the initial outcome so both survive (BR-007),
 * enforces optimistic concurrency and idempotency, and writes the immutable Audit Event.
 *
 * Regrading is a privileged correction, not a details edit: `Closed` and
 * `No Check Required` are terminal states (AD-057) and this is the only route back
 * through them.
 */

/** The four AQS outcomes (BR-005). The command matches these labels case-insensitively. */
export const FINAL_OUTCOMES = [
  'Pass',
  'Pass with issues',
  'Insufficient evidence',
  'Potential harm',
] as const;

export type FinalOutcome = (typeof FINAL_OUTCOMES)[number];

export interface RegradeCaseInput {
  /** Id of the al_outcome to regrade — the outcome, not the case. */
  outcomeId: string;
  finalOutcome: FinalOutcome;
  /** Mandatory: recorded on the outcome and the Audit Event (BR-012, NFR-AUD-01). */
  reason: string;
  /** Row version of the outcome, for optimistic concurrency. Omit to skip the check. */
  expectedRowVersion?: string | null;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface RegradeCaseOutput {
  OutcomeId: string;
  FinalOutcome: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function regradeCase(input: RegradeCaseInput): Promise<CommandResult<RegradeCaseOutput>> {
  const body: Record<string, unknown> = {
    TargetId: input.outcomeId,
    FinalOutcome: input.finalOutcome,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };

  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;

  return executeCommand<RegradeCaseOutput>('al_RegradeCase', body);
}
