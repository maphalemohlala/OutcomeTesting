import { executeCommand, type CommandResult } from './commandClient';

/**
 * Reconciling a role held in two places (AD-089). Adopt makes both sources say granted;
 * Revoke makes both say not granted. The server writes the Audit Event naming the
 * administrator who decided — which is the reason this is a command and not a schedule.
 */
export interface AdoptRoleAssignmentInput {
  userEmail: string;
  roleCode: string;
  decision: 'Adopt' | 'Revoke';
  idempotencyKey: string;
}

export interface AdoptRoleAssignmentOutput {
  MappingId: string;
  Adopted: boolean;
  AuditEventId: string;
}

export function adoptRoleAssignment(
  input: AdoptRoleAssignmentInput,
): Promise<CommandResult<AdoptRoleAssignmentOutput>> {
  return executeCommand<AdoptRoleAssignmentOutput>('al_AdoptRoleAssignment', {
    UserEmail: input.userEmail,
    RoleCode: input.roleCode,
    Decision: input.decision,
    IdempotencyKey: input.idempotencyKey,
  });
}
