import { useEffect, useState } from 'react';
import { Al_outcomecasesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import { toDetail } from './caseDetailMapping';
import type { CaseDetail } from './caseDetailMapping';

export type { CaseDetail, CaseEditValues, CaseField } from './caseDetailMapping';

export type CaseDetailState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; detail: CaseDetail };

/**
 * Reads a single Outcome Case by id from Dataverse. Row-level visibility is enforced
 * by Dataverse security (BR-012): a case the signed-in user may not see returns as
 * unavailable rather than showing partial data.
 */
export function useCaseDetail(caseId: string | undefined, reloadKey = 0): CaseDetailState {
  const [state, setState] = useState<CaseDetailState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  // Reset to loading when the route's case changes, without a setState-in-effect.
  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    Al_outcomecasesService.get(caseId)
      .then((result) => {
        if (cancelled) return;
        if (!result.success || !result.data) {
          if (!result.success) {
            logTechnical('case detail load', result.error);
          }
          setState({
            status: 'unavailable',
            reason:
              'The selected case could not be found. It may have been removed, or you may not have access to it.',
          });
          return;
        }
        setState({ status: 'ready', detail: toDetail(result.data) });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('case detail load', error);
        setState({
          status: 'unavailable',
          reason: 'This case could not be loaded right now. Please try again later.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  if (!caseId) {
    return { status: 'unavailable', reason: 'No case was requested.' };
  }

  return state;
}
