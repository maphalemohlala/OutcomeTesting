import { useEffect, useState } from 'react';
import { Al_outcomecasesal_casestatus } from '../../generated/models/Al_outcomecasesModel';
import {
  Al_outcomecasesService,
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

/**
 * The case the remediation belongs to, for the details above the table.
 *
 * The portal names the client, the adviser and the case there, and the adviser is also what
 * an unassigned action's Owner falls back to - an action nobody has been given still has an
 * adviser, and "Unassigned" against a case that names one reads as missing data.
 */
export interface RemediationCase {
  reference: string | null;
  status: string | null;
  clientName: string | null;
  adviserName: string | null;
}

export type RemediationState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      outcomeCase: RemediationCase | null;
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
      // The case is read alongside rather than required: the remediation is still readable
      // without it, and only the details line above the table is the poorer for its absence.
      Al_outcomecasesService.get(caseId).catch(() => null),
    ])
      .then(([actionResult, outcomeResult, signoffResult, caseResult]) => {
        if (cancelled) return;
        if (!actionResult.success || !outcomeResult.success || !signoffResult.success) {
          setState({
            status: 'unavailable',
            reason: 'The remediation record for this case could not be loaded from Dataverse.',
          });
          return;
        }
        const record = caseResult && caseResult.success ? caseResult.data : null;
        setState({
          status: 'ready',
          outcomeCase: record
            ? {
                reference: record.al_casereference ?? null,
                // Read off the option value rather than the formatted name: the SDK
                // leaves al_casestatusname unset on this read, so the badge drew nothing.
                // caseDetailMapping resolves the case status the same way.
                status: Al_outcomecasesal_casestatus[record.al_casestatus] ?? null,
                clientName: record.al_clientname ?? null,
                adviserName: record.al_advisername ?? null,
              }
            : null,
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
