import { executeCommand, type CommandResult } from './commandClient';

/**
 * CompleteRemediation command (BR-006, BR-008, FR-020..FR-023). An adviser drives
 * their own remediation action to Completed. The Custom API al_CompleteRemediation
 * enforces the caller, the transition, optimistic concurrency and idempotency, and
 * writes the Audit Event server-side (AD-003, AD-031).
 */

export interface CompleteRemediationInput {
  /** Id of the remediation action to complete. */
  actionId: string;
  /** Row version the client read, for optimistic concurrency. Omit to skip the check. */
  expectedRowVersion?: string | null;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface CompleteRemediationOutput {
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function completeRemediation(
  input: CompleteRemediationInput,
): Promise<CommandResult<CompleteRemediationOutput>> {
  const body: Record<string, unknown> = {
    TargetId: input.actionId,
    IdempotencyKey: input.idempotencyKey,
  };

  if (input.expectedRowVersion) {
    body.ExpectedRowVersion = input.expectedRowVersion;
  }

  return executeCommand<CompleteRemediationOutput>('al_CompleteRemediation', body);
}
