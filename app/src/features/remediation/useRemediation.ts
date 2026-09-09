import { useEffect, useState } from 'react';
import {
  Al_outcomesService,
  Al_remediationactionsService,
  Al_signoffsService,
} from '../../generated';
import {
  toAction,
  toOutcome,
  toSignoff,
  type RemediationActionRow,
  type OutcomeRow,
  type SignoffRow,
} from './remediationMapping';

export type { RemediationActionRow, OutcomeRow, SignoffRow } from './remediationMapping';

export type RemediationState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      actions: RemediationActionRow[];
      outcomes: OutcomeRow[];
      signoffs: SignoffRow[];
    };

/**
 * Reads the remediation actions (BR-006), preserved initial/final outcomes (BR-007)
 * and T&C Manager sign-offs (BR-008, FR-023) raised for one case. Read-only: the
 * complete, validate, regrade and sign-off write paths are permissioned server-side
 * commands (AD-003, AD-031). Row visibility is enforced by Dataverse security (BR-012).
 */
export function useRemediation(caseId: string | undefined, reloadKey = 0): RemediationState {
  const [state, setState] = useState<RemediationState>({ status: 'loading' });
  const [loadedFor, setLoadedFor] = useState<string | undefined>(caseId);

  if (caseId !== loadedFor) {
    setLoadedFor(caseId);
    setState({ status: 'loading' });
  }

  useEffect(() => {
    if (!caseId) return;
    let cancelled = false;

    const filter = `_al_outcomecaseid_value eq ${caseId}`;

    Promise.all([
      Al_remediationactionsService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
      Al_outcomesService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
      Al_signoffsService.getAll({ filter, orderBy: ['createdon asc'], top: 200 }),
    ])
      .then(([actionResult, outcomeResult, signoffResult]) => {
        if (cancelled) return;
        if (!actionResult.success || !outcomeResult.success || !signoffResult.success) {
          setState({
            status: 'unavailable',
            reason: 'The remediation record for this case could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          actions: actionResult.data.map(toAction),
          outcomes: outcomeResult.data.map(toOutcome),
          signoffs: signoffResult.data.map(toSignoff),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'The remediation record for this case could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [caseId, reloadKey]);

  return state;
}
