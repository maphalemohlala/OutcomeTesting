import { useEffect, useState } from 'react';
import { Al_exportbatchesService, Al_exportrecordsService } from '../../generated';
import { Al_exportbatchesal_batchstatus, type Al_exportbatches } from '../../generated/models/Al_exportbatchesModel';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { choiceLabel } from '../../lib/choiceLabel';
import { readAll } from '../../services/readAll';

interface ExportBatchRow {
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

const UNAVAILABLE = 'Exports could not be loaded from Dataverse.';

function toBatchRow(b: Al_exportbatches): ExportBatchRow {
  return {
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
  };
}

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
          batches: result.data.map(toBatchRow),
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
 * One batch's rows, in the order they were written. The batch holds exactly one file's rows, so
 * this is the file. Null when they could not be read.
 */
export async function loadExportRecords(batchId: string): Promise<Al_exportrecords[] | null> {
  const result = await readAll((o) => Al_exportrecordsService.getAll(o), {
    filter: `_al_exportbatchid_value eq ${batchId}`,
    orderBy: ['createdon asc', 'al_exportrecordid asc'],
  });
  return result.success ? result.data : null;
}
