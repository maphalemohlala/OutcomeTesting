import { executeCommand, type CommandResult } from './commandClient';

/**
 * AssignCase command (AD-003, AD-040, OD-029). A team lead or manager allocates a queued
 * case to a named reviewer. The Custom API al_AssignCase enforces the command.assign Edit
 * permission, writes the assignment history row, stamps the review instance in both
 * identity systems, moves the case to Assigned, and writes the Audit Event server-side.
 */

export interface AssignCaseInput {
  caseId: string;
  /** Work email of the assignee — the canonical cross-system identifier (OD-003, AD-010). */
  assigneeEmail: string;
  /** Which check to allocate. Omit to take the earliest unsubmitted one (BR-004). */
  reviewInstanceId?: string | null;
  /** Shared queue name recorded on the assignment row. */
  team?: string | null;
  /** Why this allocation was made; recorded on the assignment row and the Audit Event. */
  reason?: string | null;
  /** Row version of the review instance, for optimistic concurrency. */
  expectedRowVersion?: string | null;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface AssignCaseOutput {
  AssignmentId: string;
  ReviewInstanceId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function assignCase(input: AssignCaseInput): Promise<CommandResult<AssignCaseOutput>> {
  const body: Record<string, unknown> = {
    TargetId: input.caseId,
    AssigneeEmail: input.assigneeEmail,
    IdempotencyKey: input.idempotencyKey,
  };

  if (input.reviewInstanceId) body.ReviewInstanceId = input.reviewInstanceId;
  if (input.team) body.Team = input.team;
  if (input.reason) body.Reason = input.reason;
  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;

  return executeCommand<AssignCaseOutput>('al_AssignCase', body);
}
