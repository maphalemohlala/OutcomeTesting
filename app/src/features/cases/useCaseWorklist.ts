import { useEffect, useState } from 'react';
import type { CaseStatus, Outcome, ReviewRoute } from '../../types/domain';
import { REVIEW_ROUTES } from '../../types/domain';
import { Al_outcomecasesService } from '../../generated';
import { logTechnical } from '../../services/errors';
import {
  Al_outcomecasesal_casestatus,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';

export interface CaseSummary {
  id: string;
  caseReference: string;
  route: ReviewRoute | null;
  status: CaseStatus;
  owner: string | null;
  priority: string | null;
  createdOn: string | null;
  ageInDays: number;
  latestOutcome: Outcome | null;
  nextAction: string;
}

export type WorklistState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; cases: CaseSummary[] };

/** Short guidance derived from the canonical lifecycle; presentational, not a business rule. */
const NEXT_ACTION: Record<CaseStatus, string> = {
  Imported: 'Validate and route',
  'Validation Failed': 'Resolve validation errors',
  'Ready for Allocation': 'Allocate to a team or reviewer',
  Queued: 'Awaiting pickup from the queue',
  Assigned: 'Start the review',
  'Review In Progress': 'Complete the review',
  Submitted: 'Awaiting outcome',
  'Awaiting Remediation': 'Raise remediation actions',
  'Remediation In Progress': 'Complete remediation',
  'Awaiting Sign-off': 'Awaiting T&C sign-off',
  'Awaiting Recheck': 'Recheck and set final outcome',
  Closed: 'No action',
  'No Check Required': 'No action — bypassed, not graded',
};

function toRoute(name: string | undefined): ReviewRoute | null {
  return REVIEW_ROUTES.find((route) => route === name) ?? null;
}

function ageInDays(createdOn: string | undefined): number {
  if (!createdOn) return 0;
  const created = new Date(createdOn).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, Math.floor((Date.now() - created) / 86_400_000));
}

function toSummary(record: Al_outcomecases): CaseSummary {
  const status = Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus;
  return {
    id: record.al_outcomecaseid,
    caseReference: record.al_casereference,
    route: toRoute(record.al_reviewrouteidname),
    status,
    owner: record.owneridname ?? null,
    priority: record.al_priorityname ?? null,
    createdOn: record.createdon ?? null,
    ageInDays: ageInDays(record.createdon),
    // The grade lives on Response, which Wave F does not surface, so no outcome yet.
    latestOutcome: null,
    nextAction: NEXT_ACTION[status] ?? '',
  };
}

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
