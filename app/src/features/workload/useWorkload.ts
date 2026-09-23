import { useEffect, useState } from 'react';
import { Al_outcomecasesService, Al_reviewinstancesService } from '../../generated';
import { toReview } from '../cases/reviewRows';
import { logTechnical } from '../../services/errors';
import type { Discipline } from '../cases/caseCheckers';
import type { WorkloadReview } from './workload';

const TYPE: Record<Discipline, number> = { Tax: 120910200, AQS: 120910201 };
const QUEUED = 120910583;

export type WorkloadState =
  | { status: 'loading' }
  | { status: 'unavailable' }
  | { status: 'ready'; reviews: WorkloadReview[]; queued: number };

/**
 * One discipline's checks and the queued cases, as far as the caller can read them. The
 * manager's Dataverse security role limits both to what is shared with their team (AD-218),
 * so nothing here filters by team.
 *
 * Dates are the raw ISO values rather than toReview's display strings, which summarise
 * could not parse; toReview supplies only the checker and the status label.
 */
export function useWorkload(discipline: Discipline): WorkloadState {
  const [state, setState] = useState<WorkloadState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;
    Promise.all([
      Al_reviewinstancesService.getAll({ filter: `al_reviewtype eq ${TYPE[discipline]} and statecode eq 0`, top: 5000 }),
      Al_outcomecasesService.getAll({ filter: `al_casestatus eq ${QUEUED} and statecode eq 0`, top: 5000 }),
    ])
      .then(([reviews, cases]) => {
        if (cancelled) return;
        if (!reviews.success || !cases.success) {
          setState({ status: 'unavailable' });
          return;
        }
        setState({
          status: 'ready',
          reviews: reviews.data.map((row) => {
            const review = toReview(row);
            return {
              checker: review.owner,
              status: review.status,
              startedOn: row.al_startedon ?? null,
              submittedOn: row.al_submittedon ?? null,
              openedOn: row.createdon ?? null,
            };
          }),
          queued: cases.data.length,
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('workload load', error);
        setState({ status: 'unavailable' });
      });
    return () => {
      cancelled = true;
    };
  }, [discipline]);

  return state;
}
