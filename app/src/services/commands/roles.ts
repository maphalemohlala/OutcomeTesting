import { executeCommand, type CommandResult } from './commandClient';

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

export function createRole(input: CreateRoleInput): Promise<CommandResult<CreateRoleOutput>> {
  const body: Record<string, unknown> = {
    RoleName: input.roleName,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.description) body.Description = input.description;
  return executeCommand<CreateRoleOutput>('al_CreateRole', body);
}

/**
 * UpdateRole command (AD-003, AD-041, AD-044). Renames, re-describes or retires a role.
 * The role code is the stable key the assignments and permission rules reference and is
 * never changed. Retiring cascades server-side to those assignments and rules, because
 * the permission model resolves access from them rather than from the role registry.
 */
export interface UpdateRoleInput {
  roleId: string;
  /** New display name. Omit to leave unchanged. */
  roleName?: string;
  /** New description. Omit to leave unchanged; pass '' to clear it. */
  description?: string;
  /** New active state. Omit to leave unchanged. */
  active?: boolean;
  expectedRowVersion?: string | null;
  idempotencyKey: string;
}

export interface UpdateRoleOutput {
  RoleId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function updateRole(input: UpdateRoleInput): Promise<CommandResult<UpdateRoleOutput>> {
  const body: Record<string, unknown> = {
    RoleId: input.roleId,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.roleName !== undefined) body.RoleName = input.roleName;
  if (input.description !== undefined) body.Description = input.description;
  if (input.active !== undefined) body.Active = input.active;
  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;
  return executeCommand<UpdateRoleOutput>('al_UpdateRole', body);
}
