import { executeCommand, type CommandResult } from './commandClient';

/**
 * CreateUser command (AD-003, AD-041, AD-044). An administrator adds a person to the
 * application user registry (al_user), keyed on work email (AD-010). The Custom API
 * al_CreateUser enforces the permission.manage Manage permission, upserts on the work
 * email and writes the Audit Event server-side. The client only marshals the request.
 */

export interface CreateUserInput {
  fullName: string;
  workEmail: string;
  idempotencyKey: string;
}

export interface CreateUserOutput {
  UserId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function createUser(input: CreateUserInput): Promise<CommandResult<CreateUserOutput>> {
  return executeCommand<CreateUserOutput>('al_CreateUser', {
    FullName: input.fullName,
    WorkEmail: input.workEmail,
    IdempotencyKey: input.idempotencyKey,
  });
}

/**
 * UpdateUser command (AD-003, AD-041). An administrator amends an existing user's display
 * name. The Custom API al_UpdateUser enforces permission.manage Manage, applies optimistic
 * concurrency and writes the Audit Event server-side. Work email is the stable key (AD-010)
 * and is not editable here.
 */
export interface UpdateUserInput {
  userId: string;
  fullName: string;
  expectedRowVersion?: string | null;
  idempotencyKey: string;
}

export interface UpdateUserOutput {
  UserId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function updateUser(input: UpdateUserInput): Promise<CommandResult<UpdateUserOutput>> {
  const body: Record<string, unknown> = {
    UserId: input.userId,
    FullName: input.fullName,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;
  return executeCommand<UpdateUserOutput>('al_UpdateUser', body);
}

/**
 * SetUserActive command (AD-003, AD-037/OD-010). Deactivation is the sanctioned alternative
 * to hard deletion for retained data: the user row and its history are preserved, only
 * al_isactive is flipped. The Custom API al_SetUserActive enforces permission.manage Manage
 * and writes the Audit Event server-side.
 */
export interface SetUserActiveInput {
  userId: string;
  active: boolean;
  idempotencyKey: string;
}

export interface SetUserActiveOutput {
  UserId: string;
  Active: boolean;
  AuditEventId: string;
}

export function setUserActive(input: SetUserActiveInput): Promise<CommandResult<SetUserActiveOutput>> {
  return executeCommand<SetUserActiveOutput>('al_SetUserActive', {
    UserId: input.userId,
    Active: input.active,
    IdempotencyKey: input.idempotencyKey,
  });
}
