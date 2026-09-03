import { executeCommand, type CommandResult } from './commandClient';

/**
 * ResolveImportException command (AD-003, BR-002, FR-002/FR-003). Closes an import
 * exception once the row has been dealt with, which is the other half of "return invalid
 * cases with a reason": the extract's owner gets the row and its reason back, and the
 * exception is closed here saying what became of it.
 *
 * The two closures are the ones the deployed option set carries. Reopening is not offered:
 * no requirement describes it, and AD-037 forbids deleting the row to undo a closure —
 * which is why the note is mandatory and why the command refuses an exception that is not
 * open.
 */

export const IMPORT_RESOLUTIONS = ['Resolved', 'Ignored'] as const;

export type ImportResolution = (typeof IMPORT_RESOLUTIONS)[number];

/** What each closure means, shown next to the choice so it is not guessed at. */
export const IMPORT_RESOLUTION_HELP: Record<ImportResolution, string> = {
  Resolved: 'The row was corrected and re-imported, or the case exists by another route.',
  Ignored: 'The row was never a case and no import is expected.',
};

export interface ResolveImportExceptionInput {
  exceptionId: string;
  resolution: ImportResolution;
  /** Mandatory: recorded on the exception and the Audit Event (BR-012, NFR-AUD-01). */
  note: string;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface ResolveImportExceptionOutput {
  ExceptionId: string;
  Status: string;
  AuditEventId: string;
}

export function resolveImportException(
  input: ResolveImportExceptionInput,
): Promise<CommandResult<ResolveImportExceptionOutput>> {
  return executeCommand<ResolveImportExceptionOutput>('al_ResolveImportException', {
    ExceptionId: input.exceptionId,
    Resolution: input.resolution,
    Note: input.note,
    IdempotencyKey: input.idempotencyKey,
  });
}
