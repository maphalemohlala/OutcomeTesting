import { useEffect, useState } from 'react';
import { Al_exportbatchesService, Al_exportrecordsService } from '../../generated';
import { Al_exportbatchesal_batchstatus } from '../../generated/models/Al_exportbatchesModel';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { choiceLabel } from '../../lib/choiceLabel';

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
          // getAll does not return the *name formatted value, so this read fell through to
          // String(al_batchstatus) and put the raw option number on the screen — both in the
          // status column and, worse, in the status filter, whose options are built from
          // these values. The generated map is what the numbers mean.
          status: choiceLabel(Al_exportbatchesal_batchstatus, b.al_batchstatus, b.al_batchstatusname) ?? 'Unknown',
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
