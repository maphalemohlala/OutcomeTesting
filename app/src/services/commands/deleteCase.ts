import { executeCommand, type CommandResult } from './commandClient';

/**
 * DeleteCase command (requirements log row 40). Deletes the case and everything recorded
 * against it, at any stage. The server checks the case.delete capability, refuses unless the
 * typed reference matches the case's own, and keeps an audit event naming the case and the
 * reason. Nothing brings a deleted case back.
 */
export interface DeleteCaseInput {
  caseId: string;
  /** The case reference as the person typed it, to confirm. */
  confirmReference: string;
  reason: string;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface DeleteCaseOutput {
  Status: string;
  Summary: string;
  AuditEventId: string;
}

export function deleteCase(input: DeleteCaseInput): Promise<CommandResult<DeleteCaseOutput>> {
  return executeCommand<DeleteCaseOutput>('al_DeleteCase', {
    TargetId: input.caseId,
    ConfirmReference: input.confirmReference,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  });
}
