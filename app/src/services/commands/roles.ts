import { executeCommand, newCorrelationKey, type CommandResult } from './commandClient';

/**
 * CreateRole command (AD-003, AD-041, AD-044). An administrator adds a role to the
 * extensible role registry (al_role). The Custom API al_CreateRole enforces the
 * permission.manage Manage permission, upserts on the role code and writes the Audit
 * Event server-side. The client only marshals the request.
 */

export interface CreateRoleInput {
  roleName: string;
  description?: string | null;
  idempotencyKey: string;
}

export interface CreateRoleOutput {
  RoleId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function newRoleIntentKey(): string {
  return newCorrelationKey();
}

export function createRole(input: CreateRoleInput): Promise<CommandResult<CreateRoleOutput>> {
  const body: Record<string, unknown> = {
    RoleName: input.roleName,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.description) body.Description = input.description;
  return executeCommand<CreateRoleOutput>('al_CreateRole', body);
}
