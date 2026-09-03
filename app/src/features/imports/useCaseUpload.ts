import { useState } from 'react';
import { importCases, type ImportReportRow } from '../../services/commands/importCases';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { parseCaseCsv, type ValidationReportRow } from './caseUpload';

export interface UploadResult {
  batchReference: string;
  total: number;
  imported: number;
  duplicates: number;
  failed: number;
  /** Row-level rejections/skips, for the downloadable validation report (FR-002). */
  report: ValidationReportRow[];
}

export type UploadState =
  | { phase: 'idle' }
  | { phase: 'processing'; message: string }
  | { phase: 'done'; result: UploadResult }
  | { phase: 'error'; message: string };

/** The command's report row and the downloadable report row are the same shape. */
function toReportRows(rows: ImportReportRow[]): ValidationReportRow[] {
  return rows.map((row) => ({
    rowNumber: row.rowNumber,
    caseReference: row.caseReference,
    status: row.status,
    reason: row.reason,
    raw: row.raw,
  }));
}

/**
 * Uploads an Intelligent Office extract through the `al_ImportCases` command (AD-003).
 *
 * The whole import -- batch, cases, exceptions and audit event -- happens server-side in
 * one transaction, so BR-002 validation cannot be bypassed by posting to the Web API and
 * a failure part-way through leaves nothing half-written. This hook reads the file, does a
 * local parse purely to catch a file that is unusable before sending it, and marshals the
 * result.
 */
export function useCaseUpload(onUploaded: () => void) {
  const [state, setState] = useState<UploadState>({ phase: 'idle' });
  const intent = useIntentKeys();

  async function upload(file: File): Promise<void> {
    setState({ phase: 'processing', message: 'Reading file…' });
    let text: string;
    try {
      text = await file.text();
    } catch {
      setState({ phase: 'error', message: 'The file could not be read.' });
      return;
    }

    // A local pre-check, not the rule. The server re-parses and re-validates; this only
    // saves a round trip on a file that is obviously not an extract, and it is the same
    // parser, so a file it accepts is a file the command accepts.
    const parsed = parseCaseCsv(text);
    if (parsed.fatal) {
      setState({ phase: 'error', message: parsed.fatal });
      return;
    }
    if (parsed.valid.length === 0 && parsed.invalid.length === 0) {
      setState({ phase: 'error', message: 'The file has no case rows to import.' });
      return;
    }

    setState({
      phase: 'processing',
      message: `Importing ${parsed.valid.length + parsed.invalid.length} rows…`,
    });

    // One key per file, held until the import is confirmed (NFR-REL-01). Retrying the same
    // file after a timeout replays the original batch rather than logging a second one;
    // a genuinely different file — corrected and re-saved — carries a different token.
    const token = `import:${file.name}:${file.size}:${file.lastModified}`;

    const result = await importCases({
      fileName: file.name,
      csv: text,
      idempotencyKey: intent.keyFor(token),
    });

    if (!result.ok) {
      setState({ phase: 'error', message: result.message });
      // The command may have written the batch before failing, and a failure that turns
      // out to be a timed-out success leaves rows in place. Reload either way, so the
      // screen shows what Dataverse actually holds rather than what the error implies.
      onUploaded();
      return;
    }

    intent.release(token);
    setState({
      phase: 'done',
      result: {
        batchReference: result.data.batchReference,
        total: result.data.total,
        imported: result.data.imported,
        duplicates: result.data.duplicates,
        failed: result.data.failed,
        report: toReportRows(result.data.report),
      },
    });
    onUploaded();
  }

  function reset(): void {
    setState({ phase: 'idle' });
  }

  return { state, upload, reset };
}
