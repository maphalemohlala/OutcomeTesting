import { useEffect, useState } from 'react';
import { Al_exportbatchesService, Al_exportrecordsService } from '../../generated';
import { Al_exportbatchesal_batchstatus, type Al_exportbatches } from '../../generated/models/Al_exportbatchesModel';
import { choiceLabel } from '../../lib/choiceLabel';
import { isRecordId } from '../../services/odata';
import { readAll } from '../../services/readAll';
import { toExportRecordRow, type ExportRecordRow } from './exportRecordRow';


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
export type ExportRecordsState = Loaded<{ records: ExportRecordRow[] }>;

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

/**
 * One batch, and the id and generation time of every batch generated before it: its default
 * daily file starts on the day the previous batch was generated, so without them every batch
 * covered only its day before and a weekend's checks reached no default file.
 * A filtered read rather than get(), so a batch that is not there stays an empty answer
 * ("Batch not found") and only a failed read is "cannot be shown".
 */
export async function loadExportBatch(batchId: string): Promise<ExportBatchesState> {
  const result = await Al_exportbatchesService.getAll({ filter: `al_exportbatchid eq ${batchId}`, top: 1 });
  if (!result.success) return { status: 'unavailable', reason: UNAVAILABLE };
  const own = result.data[0]?.al_generatedon ? new Date(result.data[0].al_generatedon) : null;
  if (!own || Number.isNaN(own.getTime())) return { status: 'ready', batches: result.data.map(toBatchRow) };

  const earlier = await readAll((o) => Al_exportbatchesService.getAll(o), {
    select: ['al_exportbatchid', 'al_generatedon'],
    filter: `al_generatedon lt ${own.toISOString()}`,
    orderBy: ['al_generatedon desc', 'al_exportbatchid asc'],
  });
  if (!earlier.success) return { status: 'unavailable', reason: UNAVAILABLE };
  return { status: 'ready', batches: [...result.data, ...earlier.data].map(toBatchRow) };
}

/** {@link loadExportBatch} for the batch page. */
export function useExportBatch(batchId: string): ExportBatchesState {
  const [state, setState] = useState<ExportBatchesState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState(batchId);

  // Reset to loading when the route's batch changes, without a setState-in-effect.
  if (batchId !== loadedFor) {
    setLoadedFor(batchId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    // The id comes from the route, so it is untrusted until it parses as a record id.
    if (!isRecordId(batchId)) return;
    let cancelled = false;

    loadExportBatch(batchId)
      .then((loaded) => {
        if (!cancelled) setState(loaded);
      })
      .catch(() => {
        if (!cancelled) setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, [batchId]);

  // No batch has an id that is not one; the records read says what is wrong with the link.
  return isRecordId(batchId) ? state : { status: 'ready', batches: [] };
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
 * One batch's rows. Read per batch because every batch snapshots every closed case (and every
 * graded one still open), so the table grows by the whole closed caseload each time and cannot
 * be read in one go.
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
