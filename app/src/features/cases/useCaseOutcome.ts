import { useEffect, useState } from 'react';
import { Al_outcomesService } from '../../generated';
import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';

export interface CaseOutcomeRow {
  id: string;
  reference: string;
  reviewInstance: string | null;
  initialOutcome: string;
  finalOutcome: string | null;
  regraded: boolean;
  finalisedOn: string | null;
}

export type CaseOutcomeState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; outcomes: CaseOutcomeRow[] };

function date(value: string | undefined): string | null {
  if (!value) return null;
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return null;
  return new Date(time).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

function toOutcome(record: Al_outcomes): CaseOutcomeRow {
  return {
    id: record.al_outcomeid,
    reference: record.al_name?.trim() || record.al_outcomecode,
    reviewInstance: record.al_reviewinstanceidname?.trim() || null,
    initialOutcome:
      record.al_initialoutcomename ??
      Al_outcomesal_initialoutcome[record.al_initialoutcome] ??
      '—',
    finalOutcome:
      record.al_finaloutcomename ??
      (record.al_finaloutcome !== undefined
        ? Al_outcomesal_finaloutcome[record.al_finaloutcome]
        : null),
    regraded: Boolean(record.al_regradedon),
    finalisedOn: date(record.al_finalisedon),
  };
}

/**
 * The preserved initial and final outcomes recorded against one case (BR-007), one row
 * per graded review. Read-only; the regrade and sign-off write paths are server-side
 * commands (AD-003). Row visibility is enforced by Dataverse security (BR-012).
 */
export function useCaseOutcome(caseId: string | undefined): CaseOutcomeState {
  const [state, setState] = useState<CaseOutcomeState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    Al_outcomesService.getAll({
      filter: `_al_outcomecaseid_value eq ${caseId}`,
      top: 50,
    })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          setState({ status: 'unavailable' });
          return;
        }
        setState({ status: 'ready', outcomes: result.data.map(toOutcome) });
      })
      .catch(() => {
        if (cancelled) return;
        setState({ status: 'unavailable' });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId]);

  return state;
}
