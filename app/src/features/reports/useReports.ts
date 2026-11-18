import { useEffect, useMemo, useState } from 'react';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import type { Al_signoffs } from '../../generated/models/Al_signoffsModel';
import { Al_signoffsService } from '../../generated';
import { readAll } from '../../services/readAll';
import type { WorklistState } from '../cases/useCaseWorklist';

export type ReportState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      /**
       * The rows, not the totals: the page narrows them to the filtered cases before it
       * aggregates (reportFilters), so a filter change does not refetch.
       */
      outcomes: Al_outcomes[];
      actions: Al_remediationactions[];
      signoffs: Al_signoffs[];
    };

const UNAVAILABLE: ReportState = {
  status: 'unavailable',
  reason: 'Management reporting could not be loaded from Dataverse.',
};

/**
 * Management information for BR-010: outcome volumes, remediation ageing and sign-off
 * accountability, read from live Dataverse (AD-034, no Power BI). Read-only aggregation;
 * row visibility is enforced by Dataverse security (BR-012), so the figures reflect only
 * the cases the signed-in user may see.
 *
 * The outcomes and remediation actions are the rows the page's case list read already
 * fetched, so they come from `worklist` rather than a second read; only sign-offs are read
 * here. All three must have been read, as before, or the report says it cannot be shown.
 */
export function useReports(worklist: WorklistState): ReportState {
  // undefined while reading, null when the read failed.
  const [signoffs, setSignoffs] = useState<Al_signoffs[] | null | undefined>(undefined);

  useEffect(() => {
    let cancelled = false;

    readAll((o) => Al_signoffsService.getAll(o), { orderBy: ['al_signoffid asc'], select: ['al_signoffid', '_al_outcomecaseid_value', 'al_signoffdecision'] })
      .then((result) => {
        if (!cancelled) setSignoffs(result.success ? result.data : null);
      })
      .catch(() => {
        if (!cancelled) setSignoffs(null);
      });

    return () => {
      cancelled = true;
    };
  }, []);

  const outcomes = worklist.status === 'loading' ? undefined : worklist.outcomes;
  const actions = worklist.status === 'loading' ? undefined : worklist.actions;

  return useMemo<ReportState>(() => {
    if (outcomes === undefined || actions === undefined || signoffs === undefined) {
      return { status: 'loading' };
    }
    if (!outcomes || !actions || !signoffs) return UNAVAILABLE;
    return { status: 'ready', outcomes, actions, signoffs };
  }, [outcomes, actions, signoffs]);
}
