import { executeCommand, newCorrelationKey, type CommandResult } from './commandClient';

/**
 * Application RBAC write commands (AD-003, AD-041). Each runs as a Dataverse Custom
 * API that enforces the caller against the permission model server-side and writes an
 * immutable Audit Event. The client only marshals the request.
 */

export interface AssignUserRoleInput {
  userEmail: string;
  appRole?: string;
  roleCode?: string;
  idempotencyKey: string;
}

export interface AssignUserRoleOutput {
  MappingId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export interface SetPagePermissionInput {
  appRole?: string;
  roleCode?: string;
  resourceKey: string;
  accessLevel: string;
  idempotencyKey: string;
}

export interface SetPagePermissionOutput {
  PermissionId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function newPermissionIntentKey(): string {
  return newCorrelationKey();
}

export function assignUserRole(input: AssignUserRoleInput): Promise<CommandResult<AssignUserRoleOutput>> {
  const body: Record<string, unknown> = {
    UserEmail: input.userEmail,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.roleCode) body.RoleCode = input.roleCode;
  else if (input.appRole) body.AppRole = input.appRole;
  return executeCommand<AssignUserRoleOutput>('al_AssignUserRole', body);
}

export function setPagePermission(
  input: SetPagePermissionInput,
): Promise<CommandResult<SetPagePermissionOutput>> {
  const body: Record<string, unknown> = {
    ResourceKey: input.resourceKey,
    AccessLevel: input.accessLevel,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.roleCode) body.RoleCode = input.roleCode;
  else if (input.appRole) body.AppRole = input.appRole;
  return executeCommand<SetPagePermissionOutput>('al_SetPagePermission', body);
}
