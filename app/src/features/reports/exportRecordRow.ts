import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { lookupLabel } from '../cases/lookupLabel';

export interface ExportRecordRow {
  id: string;
  name: string;
  batchId: string | null;
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
 */
export function toExportRecordRow(r: Al_exportrecords): ExportRecordRow {
  const batchId = r._al_exportbatchid_value ?? null;
  const caseId = r._al_outcomecaseid_value ?? null;
  return {
    id: r.al_exportrecordid,
    name: r.al_name ?? '',
    batchId,
    caseId,
    caseReference: lookupLabel(r, 'al_outcomecaseid', r.al_outcomecaseidname) ?? '',
    adviser: r.al_advisername ?? '',
    client: r.al_clientname ?? '',
    adviceGrade: r.al_advicequalitygrade ?? '',
    record: r,
  };
}
