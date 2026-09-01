import { useEffect, useState } from 'react';
import { Al_outcomecasesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { toSummary } from './caseWorklistMapping';
import type { CaseSummary } from './caseWorklistMapping';

export type { CaseSummary } from './caseWorklistMapping';

export type WorklistState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; cases: CaseSummary[] };

/**
 * Reads the Outcome Case worklist from Dataverse. Row-level visibility is enforced
 * by Dataverse security (BR-012), so this returns only the cases the signed-in user
 * may see. Never returns sample rows.
 */
export function useCaseWorklist(): WorklistState {
  const [state, setState] = useState<WorklistState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_outcomecasesService.getAll({ orderBy: ['createdon asc'], top: 200 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          logTechnical('worklist load', result.error);
          setState({
            status: 'unavailable',
            reason: 'The case list could not be loaded right now. Please try again later.',
          });
          return;
        }
        setState({ status: 'ready', cases: result.data.map(toSummary) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('worklist load', error);
        setState({
          status: 'unavailable',
          reason: 'The case list could not be loaded right now. Please try again later.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
