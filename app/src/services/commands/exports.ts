import { executeCommand, type CommandResult } from './commandClient';

/**
 * The Trail Light export command (AD-003, AD-039, AD-034 manual only). One call produces one
 * file: GenerateExport creates the batch under the id given and snapshots the rows of the Daily
 * or Quarterly file for the check dates given. Server-side, it enforces the caller and writes an
 * immutable Audit Event.
 */

export type TrailLightFileKind = 'Daily' | 'Quarterly';

export interface GenerateExportInput {
  batchId: string;
  idempotencyKey: string;
  fileKind: TrailLightFileKind;
  /** yyyy-mm-dd, both ends included; empty for an open bound. */
  checkedFrom: string;
  checkedTo: string;
}

export interface GenerateExportOutput {
  BatchId: string;
  RowCount: string;
  Status: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function generateExport(input: GenerateExportInput): Promise<CommandResult<GenerateExportOutput>> {
  const body: Record<string, unknown> = {
    BatchId: input.batchId,
    IdempotencyKey: input.idempotencyKey,
    FileKind: input.fileKind,
  };
  if (input.checkedFrom) body.CheckedFrom = input.checkedFrom;
  if (input.checkedTo) body.CheckedTo = input.checkedTo;
  return executeCommand<GenerateExportOutput>('al_GenerateExport', body);
}
