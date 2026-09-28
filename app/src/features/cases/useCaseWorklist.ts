import { useEffect, useState } from 'react';
import {
  Al_outcomecasesService,
  Al_outcomesService,
  Al_remediationactionsService,
} from '../../generated';
import { logTechnical } from '../../services/errors';
import { gradesByCase, toSummary } from './caseWorklistMapping';
import { remediationByCase, type CaseRemediation } from './caseRemediation';
import type { CaseOutcomeGrades, CaseSummary } from './caseWorklistMapping';

export type { CaseSummary } from './caseWorklistMapping';

export type WorklistState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      cases: CaseSummary[];
      /**
       * False when remediation actions could not be read. The remediation filter and column
       * are then hidden rather than reading every case as having raised none.
       */
      remediationKnown: boolean;
    };

const UNAVAILABLE = 'The case list could not be loaded right now. Please try again later.';

/**
 * Reads the Outcome Case worklist from Dataverse, joined to the BR-007 outcome so a row
 * can show and be filtered by the grade it currently stands on. Row-level visibility is
 * enforced by Dataverse security (BR-012), so this returns only the cases the signed-in
 * user may see. Never returns sample rows.
 */
export function useCaseWorklist(): WorklistState {
  const [state, setState] = useState<WorklistState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_outcomecasesService.getAll({ orderBy: ['createdon asc'], top: 5000 }),
      Al_outcomesService.getAll({ top: 5000 }),
      Al_remediationactionsService.getAll({ top: 5000 }),
    ])
      .then(([cases, outcomes, actions]) => {
        if (cancelled) return;
        if (!cases.success) {
          logTechnical('worklist load', cases.error);
          setState({ status: 'unavailable', reason: UNAVAILABLE });
          return;
        }
        // A caller without read on al_outcome still gets their cases; the grade column
        // then reads as not yet graded rather than the whole worklist going unavailable.
        if (!outcomes.success) logTechnical('worklist outcome join', outcomes.error);
        const grades: Map<string, CaseOutcomeGrades> = outcomes.success
          ? gradesByCase(outcomes.data)
          : new Map();
        // The same degradation for remediation: the cases still list, without the column.
        if (!actions.success) logTechnical('worklist remediation join', actions.error);
        const remediation: Map<string, CaseRemediation> = actions.success
          ? remediationByCase(actions.data)
          : new Map();

        setState({
          status: 'ready',
          cases: cases.data.map((record) => ({
            ...toSummary(record, grades.get(record.al_outcomecaseid)),
            remediation: actions.success ? (remediation.get(record.al_outcomecaseid) ?? null) : undefined,
          })),
          remediationKnown: actions.success,
        });
      })
      .catch((error) => {
        if (cancelled) return;
        logTechnical('worklist load', error);
        setState({ status: 'unavailable', reason: UNAVAILABLE });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
