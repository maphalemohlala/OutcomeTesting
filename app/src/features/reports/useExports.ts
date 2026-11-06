import { useEffect, useState } from 'react';
import { Al_exportbatchesService, Al_exportrecordsService } from '../../generated';
import { Al_exportbatchesal_batchstatus } from '../../generated/models/Al_exportbatchesModel';
import { choiceLabel } from '../../lib/choiceLabel';
import { isRecordId } from '../../services/odata';
import { readAll } from '../../services/readAll';
import { toExportRecordRow, type ExportRecordRow } from './exportRecordRow';

export type { ExportRecordRow } from './exportRecordRow';

export interface ExportBatchRow {
  id: string;
  name: string;
  code: string;
  status: string;
  generatedOn: string | null;
  rowCount: number;
}

type Loaded<T> =
  | { status: 'loading' }
  | { status: 'unavailable'; reason: string }
  | ({ status: 'ready' } & T);

export type ExportBatchesState = Loaded<{ batches: ExportBatchRow[] }>;
export type ExportRecordsState = Loaded<{ records: ExportRecordRow[] }>;

const UNAVAILABLE = 'Exports could not be loaded from Dataverse.';

export function useExportBatches(reloadKey: number): ExportBatchesState {
  const [state, setState] = useState<ExportBatchesState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    readAll((o) => Al_exportbatchesService.getAll(o), { orderBy: ['createdon desc', 'al_exportbatchid asc'] })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          setState({ status: 'unavailable', reason: UNAVAILABLE });
          return;
        }

        setState({
          status: 'ready',
          batches: result.data.map((b) => ({
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
          })),
        });
      })
      .catch(() => {
        if (!cancelled) setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}

/**
 * One batch's rows. Read per batch because every batch snapshots every closed case, so the
 * table grows by the whole closed caseload each time and cannot be read in one go.
 */
export function useExportRecords(batchId: string): ExportRecordsState {
  const [state, setState] = useState<ExportRecordsState>({ status: 'loading' });

  useEffect(() => {
    // The id comes from the route, so it is untrusted until it parses as a record id.
    if (!isRecordId(batchId)) return;
    let cancelled = false;

    readAll((o) => Al_exportrecordsService.getAll(o), {
      filter: `_al_exportbatchid_value eq ${batchId}`,
      orderBy: ['createdon desc', 'al_exportrecordid asc'],
    })
      .then((result) => {
        if (cancelled) return;
        setState(
          result.success
            ? { status: 'ready', records: result.data.map((row) => toExportRecordRow(row)) }
            : { status: 'unavailable', reason: UNAVAILABLE },
        );
      })
      .catch(() => {
        if (!cancelled) setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [batchId]);

  return isRecordId(batchId) ? state : { status: 'unavailable', reason: UNAVAILABLE };
}
