import { useEffect, useState } from 'react';
import { isRecordId } from '../../services/odata';
import { Al_outcomesService } from '../../generated';
import { toOutcome, type CaseOutcome } from './caseOutcomeMapping';

/** One outcome row, as the outcome summary, the accountability editor and the recheck read it. */
export type CaseOutcomeRow = CaseOutcome;

export type CaseOutcomeState =
  | { status: 'unavailable' }
  | { status: 'loading' }
  | { status: 'ready'; outcomes: CaseOutcomeRow[] };

/**
 * The preserved initial and final outcomes recorded against one case (BR-007), one row
 * per graded review. Read-only; the regrade and sign-off write paths are server-side
 * commands (AD-003). Row visibility is enforced by Dataverse security (BR-012).
 */
export function useCaseOutcome(caseId: string | undefined, reloadKey = 0): CaseOutcomeState {
  const [state, setState] = useState<CaseOutcomeState>({ status: 'loading' });
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
    void reloadKey; // named in the dependency list below; recording accountability re-reads.

    Al_outcomesService.getAll({
      filter: `_al_outcomecaseid_value eq ${caseId}`,
      // Creation order: the latest outcome is the last one, which the recheck relies on.
      orderBy: ['createdon asc', 'al_outcomeid asc'],
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
