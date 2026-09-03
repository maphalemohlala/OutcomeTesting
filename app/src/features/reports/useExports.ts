import { useEffect, useState } from 'react';
import { Al_exportbatchesService, Al_exportrecordsService } from '../../generated';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';

export interface ExportBatchRow {
  id: string;
  name: string;
  code: string;
  status: string;
  generatedOn: string | null;
  rowCount: number;
}

export interface ExportRecordRow {
  id: string;
  name: string;
  batchId: string | null;
  batchName: string;
  adviser: string;
  client: string;
  adviceGrade: string;
  /** Kept whole so the AD-039 twenty-column file is written from what was snapshotted. */
  record: Al_exportrecords;
}

export type ExportsState =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | { status: 'ready'; batches: ExportBatchRow[]; records: ExportRecordRow[] };

export function useExports(reloadKey: number): ExportsState {
  const [state, setState] = useState<ExportsState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_exportbatchesService.getAll({ orderBy: ['createdon desc'], top: 200 }),
      Al_exportrecordsService.getAll({ orderBy: ['createdon desc'], top: 5000 }),
    ])
      .then(([batchResult, recordResult]) => {
        if (cancelled) return;
        if (!batchResult.success || !recordResult.success) {
          setState({ status: 'unavailable', reason: 'Exports could not be loaded from Dataverse.' });
          return;
        }

        const batches: ExportBatchRow[] = batchResult.data.map((b) => ({
          id: b.al_exportbatchid,
          name: b.al_name ?? '',
          code: b.al_exportbatchcode ?? '',
          status: b.al_batchstatusname ?? String(b.al_batchstatus ?? ''),
          generatedOn: b.al_generatedon ?? null,
          rowCount: Number(b.al_rowcount ?? 0),
        }));

        const records: ExportRecordRow[] = recordResult.data.map((r) => ({
          id: r.al_exportrecordid,
          name: r.al_name ?? '',
          batchId: r._al_exportbatchid_value ?? null,
          batchName: r.al_exportbatchidname ?? '',
          adviser: r.al_advisername ?? '',
          client: r.al_clientname ?? '',
          adviceGrade: r.al_advicequalitygrade ?? '',
          record: r,
        }));

        setState({ status: 'ready', batches, records });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'unavailable', reason: 'Exports could not be loaded from Dataverse.' });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
