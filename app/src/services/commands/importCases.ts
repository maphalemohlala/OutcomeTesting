import { executeCommand, type CommandResult } from './commandClient';

/**
 * ImportCases command (AD-003, BR-001, BR-002, FR-002). The extract is sent as text and
 * validated server-side: the plug-in parses it, logs the batch, creates one case per
 * valid row, skips references Dataverse already holds so a re-upload is idempotent, and
 * records every rejected row as an Import Exception carrying the reason.
 *
 * The Code App keeps its own copy of the parser, but only to show a user what will be
 * rejected before they upload. This command is what BR-002 actually is — before it,
 * validation lived only in the browser and a caller posting to the Web API met none of it.
 */

export interface ImportCasesInput {
  fileName: string;
  /** The raw CSV text of the extract. */
  csv: string;
  /** Stable idempotency key for this upload; reuse across retries. */
  idempotencyKey: string;
}

/** Raw shape returned by the Custom API: counts cross the wire as strings. */
export interface ImportCasesOutput {
  BatchId: string;
  BatchReference: string;
  Total: string;
  Imported: string;
  Duplicates: string;
  Failed: string;
  Report: string;
  AuditEventId: string;
}

/** One rejected or skipped row, as the command reports it (FR-002). */
export interface ImportReportRow {
  rowNumber: number;
  caseReference: string | null;
  status: string;
  reason: string;
  raw: string;
}

export interface ImportSummary {
  batchId: string;
  batchReference: string;
  total: number;
  imported: number;
  duplicates: number;
  failed: number;
  report: ImportReportRow[];
}

function count(value: unknown): number {
  const parsed = Number.parseInt(String(value ?? ''), 10);
  return Number.isFinite(parsed) ? parsed : 0;
}

/**
 * Reads the report the command returned. A malformed or absent payload degrades to an
 * empty report rather than failing the upload: the rows are already Import Exceptions in
 * Dataverse and the screen lists them from there, so the download is the only thing lost.
 */
function parseReport(raw: unknown): ImportReportRow[] {
  if (typeof raw !== 'string' || raw.trim() === '') return [];
  try {
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    return parsed.map((row) => {
      const record = row as Record<string, unknown>;
      return {
        rowNumber: count(record.rowNumber),
        caseReference: typeof record.caseReference === 'string' ? record.caseReference : null,
        status: String(record.status ?? 'Rejected'),
        reason: String(record.reason ?? ''),
        raw: String(record.raw ?? ''),
      };
    });
  } catch {
    return [];
  }
}

export function toImportSummary(data: ImportCasesOutput): ImportSummary {
  return {
    batchId: data.BatchId,
    batchReference: data.BatchReference,
    total: count(data.Total),
    imported: count(data.Imported),
    duplicates: count(data.Duplicates),
    failed: count(data.Failed),
    report: parseReport(data.Report),
  };
}

export async function importCases(
  input: ImportCasesInput,
): Promise<CommandResult<ImportSummary>> {
  const result = await executeCommand<ImportCasesOutput>('al_ImportCases', {
    FileName: input.fileName,
    Csv: input.csv,
    IdempotencyKey: input.idempotencyKey,
  });

  return result.ok ? { ok: true, data: toImportSummary(result.data) } : result;
}
