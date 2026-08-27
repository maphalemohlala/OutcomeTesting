import { useState } from 'react';
import {
  Al_importbatchesService,
  Al_importexceptionsService,
  Al_outcomecasesService,
} from '../../generated';
import type { Al_importbatchesal_batchstatus } from '../../generated/models/Al_importbatchesModel';
import type { Al_importexceptionsal_exceptionstatus } from '../../generated/models/Al_importexceptionsModel';
import { parseCaseCsv, type ParsedCase, type RowError, type ValidationReportRow } from './caseUpload';

const BATCH_STATUS_VALIDATING: Al_importbatchesal_batchstatus = 120910731;
const BATCH_STATUS_COMPLETED: Al_importbatchesal_batchstatus = 120910732;
const EXCEPTION_STATUS_OPEN: Al_importexceptionsal_exceptionstatus = 120910740;
const EXCEPTION_STATUS_IGNORED: Al_importexceptionsal_exceptionstatus = 120910742;

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

function odataEscape(value: string): string {
  return value.replace(/'/g, "''");
}

async function referenceExists(reference: string): Promise<boolean> {
  const result = await Al_outcomecasesService.getAll({
    filter: `al_casereference eq '${odataEscape(reference)}'`,
    top: 1,
  });
  return result.success && result.data.length > 0;
}

async function recordException(
  batchId: string,
  batchCode: string,
  rowNumber: number,
  caseReference: string | null,
  reason: string,
  raw: string,
  status: Al_importexceptionsal_exceptionstatus,
): Promise<void> {
  await Al_importexceptionsService.create({
    al_name: `${batchCode}-R${rowNumber}`,
    al_importexceptioncode: `${batchCode}-R${rowNumber}`,
    al_exceptionstatus: status,
    'al_importbatchid@odata.bind': `/al_importbatches(${batchId})`,
    al_reason: reason.slice(0, 2000),
    al_rownumber: rowNumber,
    al_casereference: caseReference ?? undefined,
    al_rawdata: raw,
    statecode: 0,
  });
}

/**
 * Uploads an Intelligent Office extract: logs an Import Batch, creates one
 * Outcome Case per valid row (skipping references already in Dataverse so
 * re-uploads are idempotent, BR-001), and records every rejected row as an
 * Import Exception. Row visibility on read is still enforced by Dataverse.
 */
export function useCaseUpload(onUploaded: () => void) {
  const [state, setState] = useState<UploadState>({ phase: 'idle' });

  async function upload(file: File): Promise<void> {
    setState({ phase: 'processing', message: 'Reading file…' });
    let text: string;
    try {
      text = await file.text();
    } catch {
      setState({ phase: 'error', message: 'The file could not be read.' });
      return;
    }

    const parsed = parseCaseCsv(text);
    if (parsed.fatal) {
      setState({ phase: 'error', message: parsed.fatal });
      return;
    }
    if (parsed.valid.length === 0 && parsed.invalid.length === 0) {
      setState({ phase: 'error', message: 'The file has no case rows to import.' });
      return;
    }

    const batchCode = `BATCH-${Date.now()}`;
    const total = parsed.valid.length + parsed.invalid.length;
    setState({ phase: 'processing', message: 'Creating import batch…' });

    const batch = await Al_importbatchesService.create({
      al_name: file.name,
      al_importbatchcode: batchCode,
      al_batchstatus: BATCH_STATUS_VALIDATING,
      al_source: file.name.slice(0, 400),
      al_importedon: new Date().toISOString(),
      al_totalrows: total,
      al_importedcount: 0,
      al_exceptioncount: 0,
      statecode: 0,
    });
    if (!batch.success || !batch.data) {
      setState({ phase: 'error', message: 'The import batch could not be created in Dataverse.' });
      return;
    }
    const batchId = batch.data.al_importbatchid;

    let imported = 0;
    let duplicates = 0;
    let failed = 0;
    const report: ValidationReportRow[] = [];

    for (let i = 0; i < parsed.valid.length; i += 1) {
      const row: ParsedCase = parsed.valid[i];
      setState({ phase: 'processing', message: `Importing case ${i + 1} of ${parsed.valid.length}…` });
      try {
        if (await referenceExists(row.reference)) {
          duplicates += 1;
          const reason =
            'IO reference already exists in Dataverse — skipped to avoid a duplicate case.';
          report.push({
            rowNumber: row.rowNumber,
            caseReference: row.reference,
            status: 'Duplicate (skipped)',
            reason,
            raw: row.raw,
          });
          await recordException(
            batchId,
            batchCode,
            row.rowNumber,
            row.reference,
            reason,
            row.raw,
            EXCEPTION_STATUS_IGNORED,
          );
          continue;
        }
        const created = await Al_outcomecasesService.create(row.record);
        if (created.success) {
          imported += 1;
        } else {
          failed += 1;
          const reason = 'Dataverse rejected this case. Check the values and try again.';
          report.push({
            rowNumber: row.rowNumber,
            caseReference: row.reference,
            status: 'Failed',
            reason,
            raw: row.raw,
          });
          await recordException(
            batchId,
            batchCode,
            row.rowNumber,
            row.reference,
            reason,
            row.raw,
            EXCEPTION_STATUS_OPEN,
          );
        }
      } catch {
        failed += 1;
        const reason = 'Dataverse rejected this case. Check the values and try again.';
        report.push({
          rowNumber: row.rowNumber,
          caseReference: row.reference,
          status: 'Failed',
          reason,
          raw: row.raw,
        });
        await recordException(
          batchId,
          batchCode,
          row.rowNumber,
          row.reference,
          reason,
          row.raw,
          EXCEPTION_STATUS_OPEN,
        );
      }
    }

    for (const bad of parsed.invalid as RowError[]) {
      report.push({
        rowNumber: bad.rowNumber,
        caseReference: bad.caseReference,
        status: 'Invalid',
        reason: bad.reason,
        raw: bad.raw,
      });
      try {
        await recordException(
          batchId,
          batchCode,
          bad.rowNumber,
          bad.caseReference,
          bad.reason,
          bad.raw,
          EXCEPTION_STATUS_OPEN,
        );
      } catch {
        failed += 1;
      }
    }

    const exceptionCount = total - imported;
    await Al_importbatchesService.update(batchId, {
      al_batchstatus: BATCH_STATUS_COMPLETED,
      al_importedcount: imported,
      al_exceptioncount: exceptionCount,
    });

    setState({
      phase: 'done',
      result: { batchReference: batchCode, total, imported, duplicates, failed, report },
    });
    onUploaded();
  }

  function reset(): void {
    setState({ phase: 'idle' });
  }

  return { state, upload, reset };
}
