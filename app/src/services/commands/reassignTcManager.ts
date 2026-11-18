import { executeCommand, type CommandResult } from './commandClient';

/**
 * ReassignTcManager command (requirements log row 4). Hands one case's remedial action and
 * sign-off to another T&C Manager in place of the one mapped to its adviser; an empty manager
 * hands it back. The server checks the case.reassignmanager capability and that the person
 * holds the T&C Supervisor role.
 */
export interface ReassignTcManagerInput {
  caseId: string;
  /** Contact id, or '' to hand the case back to the adviser mapping. */
  managerId: string;
  reason: string;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface ReassignTcManagerOutput {
  Status: string;
  Summary: string;
  AuditEventId: string;
}

export function reassignTcManager(
  input: ReassignTcManagerInput,
): Promise<CommandResult<ReassignTcManagerOutput>> {
  return executeCommand<ReassignTcManagerOutput>('al_ReassignTcManager', {
    TargetId: input.caseId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
    ManagerId: input.managerId,
  });
}
