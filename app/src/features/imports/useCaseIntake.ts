import { useEffect, useState } from 'react';
import { Al_importbatchesService, Al_importexceptionsService } from '../../generated';
import {
  Al_importbatchesal_batchstatus,
  type Al_importbatches,
} from '../../generated/models/Al_importbatchesModel';
import {
  Al_importexceptionsal_exceptionstatus,
  type Al_importexceptions,
} from '../../generated/models/Al_importexceptionsModel';

export interface BatchSummary {
  id: string;
  reference: string;
  name: string;
  source: string;
  status: string;
  importedOn: string | null;
  totalRows: number | null;
  importedCount: number | null;
  exceptionCount: number | null;
  owner: string | null;
}

export interface ExceptionSummary {
  id: string;
  batch: string | null;
  rowNumber: number | null;
  caseReference: string | null;
  reason: string;
  status: string;
  resolvedOn: string | null;
  /** What was done about the row when it was closed. Empty while it is still open. */
  resolutionNote: string | null;
  /** Only an open exception can be closed (al_ResolveImportException refuses the rest). */
  isOpen: boolean;
}

export type IntakeState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; batches: BatchSummary[]; exceptions: ExceptionSummary[] };

export interface IntakeView {
  state: IntakeState;
  reload: () => void;
}

function formatDate(value: string | undefined): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toLocaleDateString();
}

function toBatch(record: Al_importbatches): BatchSummary {
  return {
    id: record.al_importbatchid,
    reference: record.al_importbatchcode,
    name: record.al_name,
    source: record.al_source,
    status: Al_importbatchesal_batchstatus[record.al_batchstatus] ?? 'Unknown',
    importedOn: formatDate(record.al_importedon),
    totalRows: record.al_totalrows ?? null,
    importedCount: record.al_importedcount ?? null,
    exceptionCount: record.al_exceptioncount ?? null,
    owner: record.owneridname ?? null,
  };
}

function toException(record: Al_importexceptions): ExceptionSummary {
  const status = Al_importexceptionsal_exceptionstatus[record.al_exceptionstatus] ?? 'Unknown';
  return {
    id: record.al_importexceptionid,
    batch: record.al_importbatchidname ?? null,
    rowNumber: record.al_rownumber ?? null,
    caseReference: record.al_casereference ?? null,
    reason: record.al_reason,
    status,
    resolvedOn: formatDate(record.al_resolvedon),
    resolutionNote: record.al_resolutionnote ?? null,
    // Read from the label rather than the raw option value, so an unrecognised status
    // reads as not-open and the screen offers no action it cannot complete.
    isOpen: status === 'Open',
  };
}

/**
 * Reads Import Batches and their Import Exceptions from Dataverse (FR-001 to FR-003).
 * Row-level visibility is enforced by Dataverse security (BR-012). Never returns sample rows.
 */
export function useCaseIntake(): IntakeView {
  const [state, setState] = useState<IntakeState>({ status: 'loading' });
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_importbatchesService.getAll({ orderBy: ['createdon desc'], top: 200 }),
      Al_importexceptionsService.getAll({ orderBy: ['al_rownumber asc'], top: 500 }),
    ])
      .then(([batchResult, exceptionResult]) => {
        if (cancelled) return;
        if (!batchResult.success || !exceptionResult.success) {
          setState({
            status: 'unavailable',
            reason: 'The intake batches could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          batches: batchResult.data.map(toBatch),
          exceptions: exceptionResult.data.map(toException),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'The intake batches could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return { state, reload: () => setReloadKey((key) => key + 1) };
}
