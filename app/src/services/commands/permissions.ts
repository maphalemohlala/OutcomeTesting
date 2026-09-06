import { executeCommand, type CommandResult } from './commandClient';

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

export function assignUserRole(input: AssignUserRoleInput): Promise<CommandResult<AssignUserRoleOutput>> {
  const body: Record<string, unknown> = {
    UserEmail: input.userEmail,
    IdempotencyKey: input.idempotencyKey,
  };
  // Both keys are always sent, with the unused one empty. The Custom API declares them
  // optional, but the platform's request validator still refused a call that omitted
  // AppRole entirely — which is every web role assignment. The plug-in branches on
  // RoleCode first and treats an empty string as absent, so this changes nothing
  // server-side and stops the refusal.
  body.RoleCode = input.roleCode ?? '';
  body.AppRole = input.roleCode ? '' : (input.appRole ?? '');
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
  body.RoleCode = input.roleCode ?? '';
  body.AppRole = input.roleCode ? '' : (input.appRole ?? '');
  return executeCommand<SetPagePermissionOutput>('al_SetPagePermission', body);
}

export interface SetActiveInput {
  id: string;
  active: boolean;
  idempotencyKey: string;
}

export interface SetActiveOutput {
  Active: boolean;
  AuditEventId: string;
}

/**
 * Withdraws or restores a role assignment. Changing someone's role is a withdraw plus a
 * fresh assignUserRole, because the mapping's business code embeds the role.
 */
export function setRoleAssignmentActive(
  input: SetActiveInput,
): Promise<CommandResult<SetActiveOutput>> {
  return executeCommand<SetActiveOutput>('al_SetRoleAssignmentActive', {
    MappingId: input.id,
    Active: input.active,
    IdempotencyKey: input.idempotencyKey,
  });
}

/**
 * Withdraws or restores a permission override rule. Withdrawing drops the override so the
 * role falls back to the code default; to deny explicitly, set the rule level to None.
 */
export function setPermissionRuleActive(
  input: SetActiveInput,
): Promise<CommandResult<SetActiveOutput>> {
  return executeCommand<SetActiveOutput>('al_SetPermissionRuleActive', {
    PermissionId: input.id,
    Active: input.active,
    IdempotencyKey: input.idempotencyKey,
  });
}
