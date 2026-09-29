import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { lookupLabel } from '../cases/lookupLabel';

export interface ExportRecordRow {
  id: string;
  name: string;
  batchId: string | null;
  batchName: string;
  /** The case the row was snapshotted from, so the batch page can link to it. */
  caseId: string | null;
  caseReference: string;
  adviser: string;
  client: string;
  adviceGrade: string;
  /** Kept whole so the AD-039 Trail Light file is written from what was snapshotted. */
  record: Al_exportrecords;
}

/**
 * One export record as the Exports page lists it. Kept out of useExports so it can be tested:
 * that module imports the generated services, which do not load under vitest.
 *
 * The batch name comes from the lookup's formatted-value annotation, as lookupLabel reads it
 * for the worklist's owner. getAll leaves al_exportbatchidname empty, so reading that field
 * alone left the Batch column blank on every row (DEV, 2026-09-28). Where no annotation is
 * sent, the batch the page loaded alongside the records supplies the name.
 */
export function toExportRecordRow(r: Al_exportrecords, batchNames: Map<string, string>): ExportRecordRow {
  const batchId = r._al_exportbatchid_value ?? null;
  const caseId = r._al_outcomecaseid_value ?? null;
  return {
    id: r.al_exportrecordid,
    name: r.al_name ?? '',
    batchId,
    batchName:
      lookupLabel(r, 'al_exportbatchid', r.al_exportbatchidname) ??
      (batchId ? batchNames.get(batchId) : undefined) ??
      '',
    caseId,
    caseReference: lookupLabel(r, 'al_outcomecaseid', r.al_outcomecaseidname) ?? '',
    adviser: r.al_advisername ?? '',
    client: r.al_clientname ?? '',
    adviceGrade: r.al_advicequalitygrade ?? '',
    record: r,
  };
}
