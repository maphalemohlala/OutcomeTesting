import { executeCommand, newCorrelationKey, type CommandResult } from './commandClient';

/**
 * UpdateCaseDetails command (AD-003, AD-036, AD-041). A manager amends the editable
 * case header — status, review route, priority, due date — with a mandatory reason.
 * The Custom API al_UpdateCaseDetails enforces the page.cases Edit permission,
 * optimistic concurrency and idempotency, and writes the Audit Event server-side.
 */

export interface UpdateCaseDetailsInput {
  caseId: string;
  /** New al_casestatus option value. Omit to leave unchanged. */
  status?: number | null;
  /** New al_reviewroute id. Omit to leave unchanged. */
  routeId?: string | null;
  /** New al_priority option value. Omit to leave unchanged. */
  priority?: number | null;
  /** New due date as yyyy-MM-dd. Omit to leave unchanged. */
  dueDate?: string | null;
  /**
   * Other editable case fields keyed by al_outcomecase logical name. Choice fields take
   * the numeric option value; dates take yyyy-MM-dd; text takes the string. Only fields
   * the server allowlists are accepted, and every change is audited server-side.
   */
  fields?: Record<string, string | number | null | undefined>;
  /** Mandatory reason, recorded on the Audit Event. */
  reason: string;
  /** Row version read by the client, for optimistic concurrency. */
  expectedRowVersion?: string | null;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface UpdateCaseDetailsOutput {
  CaseId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function newCaseEditIntentKey(): string {
  return newCorrelationKey();
}

export function updateCaseDetails(
  input: UpdateCaseDetailsInput,
): Promise<CommandResult<UpdateCaseDetailsOutput>> {
  const body: Record<string, unknown> = {
    TargetId: input.caseId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };

  if (input.status != null) body.Status = String(input.status);
  if (input.routeId) body.RouteId = input.routeId;
  if (input.priority != null) body.Priority = String(input.priority);
  if (input.dueDate) body.DueDate = input.dueDate;
  if (input.expectedRowVersion) body.ExpectedRowVersion = input.expectedRowVersion;

  if (input.fields) {
    const payload: Record<string, string> = {};
    for (const [key, value] of Object.entries(input.fields)) {
      if (value === undefined || value === null) continue;
      payload[key] = String(value);
    }
    if (Object.keys(payload).length > 0) body.Fields = JSON.stringify(payload);
  }

  return executeCommand<UpdateCaseDetailsOutput>('al_UpdateCaseDetails', body, 'al_outcomecases');
}
