import { useEffect, useState } from 'react';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import type { Al_signoffs } from '../../generated/models/Al_signoffsModel';
import {
  Al_outcomesService,
  Al_remediationactionsService,
  Al_signoffsService,
} from '../../generated';

export type { OutcomeVolume, AgeingBucket, ReportData } from './reportAggregate';

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

/**
 * Management information for BR-010: outcome volumes, remediation ageing and sign-off
 * accountability, read from live Dataverse (AD-034, no Power BI). Read-only aggregation;
 * row visibility is enforced by Dataverse security (BR-012), so the figures reflect only
 * the cases the signed-in user may see.
 */
export function useReports(): ReportState {
  const [state, setState] = useState<ReportState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_outcomesService.getAll({ top: 5000 }),
      Al_remediationactionsService.getAll({ top: 5000 }),
      Al_signoffsService.getAll({ top: 5000 }),
    ])
      .then(([outcomes, actions, signoffs]) => {
        if (cancelled) return;
        if (!outcomes.success || !actions.success || !signoffs.success) {
          setState({
            status: 'unavailable',
            reason: 'Management reporting could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          outcomes: outcomes.data,
          actions: actions.data,
          signoffs: signoffs.data,
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'Management reporting could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
