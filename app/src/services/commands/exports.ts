import { executeCommand, newCorrelationKey, type CommandResult } from './commandClient';

/**
 * Trail Light export commands (AD-003, AD-039, AD-034 manual only). CreateExportBatch
 * opens a Draft run; GenerateExport snapshots each Closed case into the AD-039 20-column
 * shape and marks the batch Generated. Both are server-side Custom APIs that enforce the
 * caller and write an immutable Audit Event.
 */

export interface CreateExportBatchInput {
  name?: string;
  idempotencyKey: string;
}

export interface CreateExportBatchOutput {
  BatchId: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export interface GenerateExportInput {
  batchId: string;
  idempotencyKey: string;
}

export interface GenerateExportOutput {
  BatchId: string;
  RowCount: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function newExportIntentKey(): string {
  return newCorrelationKey();
}

export function createExportBatch(
  input: CreateExportBatchInput,
): Promise<CommandResult<CreateExportBatchOutput>> {
  const body: Record<string, unknown> = { IdempotencyKey: input.idempotencyKey };
  if (input.name) {
    body.Name = input.name;
  }
  return executeCommand<CreateExportBatchOutput>('al_CreateExportBatch', body);
}

export function generateExport(input: GenerateExportInput): Promise<CommandResult<GenerateExportOutput>> {
  return executeCommand<GenerateExportOutput>('al_GenerateExport', {
    BatchId: input.batchId,
    IdempotencyKey: input.idempotencyKey,
  });
}
