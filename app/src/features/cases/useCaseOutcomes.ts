import { useEffect, useState } from 'react';
import { isRecordId } from '../../services/odata';
import { Al_outcomesService } from '../../generated';
import { toOutcome, type CaseOutcome } from './caseOutcomeMapping';

export type { CaseOutcome };

export type CaseOutcomesState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; outcomes: CaseOutcome[] };

/**
 * Lists the outcomes recorded against one case (BR-007). A Tax-then-AQS case records an
 * outcome per review instance, so this is a list rather than a single row.
 *
 * Row visibility is enforced by Dataverse security (BR-012); a caller who cannot read
 * al_outcome gets `unavailable` rather than an empty list, because "no outcomes" and
 * "not allowed to see them" must not present the same way on a screen that offers to
 * change a grade.
 */
export function useCaseOutcomes(caseId: string | undefined, reloadKey = 0): CaseOutcomesState {
  const [state, setState] = useState<CaseOutcomesState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    // The id comes from the route, so it is untrusted until it parses as a record id;
    // an id that is not one never reaches a filter. The unavailable state for that case
    // is derived below rather than set here.
    if (!isRecordId(caseId)) return;
    let cancelled = false;

    Al_outcomesService.getAll({
      filter: `_al_outcomecaseid_value eq ${caseId}`,
      orderBy: ['createdon asc'],
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
  }, [caseId, reloadKey]);

  if (caseId && !isRecordId(caseId)) {
    return { status: 'unavailable' };
  }

  return state;
}
