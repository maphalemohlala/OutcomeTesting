import type { CaseStatus, Outcome, ReviewRoute } from '../../types/domain';
import { REVIEW_ROUTES } from '../../types/domain';
import {
  Al_outcomecasesal_casestatus,
  Al_outcomecasesal_priority,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';
import { choiceLabel } from './choiceLabel';
import { lookupLabel } from './lookupLabel';

/**
 * Pure record-to-row mapping, kept free of the generated services so it stays unit
 * testable (the Power Apps data SDK cannot be imported under vitest).
 */

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

function toRoute(name: string | null): ReviewRoute | null {
  return REVIEW_ROUTES.find((route) => route === name) ?? null;
}

function ageInDays(createdOn: string | undefined): number {
  if (!createdOn) return 0;
  const created = new Date(createdOn).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, Math.floor((Date.now() - created) / 86_400_000));
}

export function toSummary(record: Al_outcomecases): CaseSummary {
  const status = Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus;
  return {
    id: record.al_outcomecaseid,
    caseReference: record.al_casereference,
    route: toRoute(lookupLabel(record, 'al_reviewrouteid', record.al_reviewrouteidname)),
    status,
    owner: lookupLabel(record, 'ownerid', record.owneridname),
    priority: choiceLabel(Al_outcomecasesal_priority, record.al_priority, record.al_priorityname),
    createdOn: record.createdon ?? null,
    ageInDays: ageInDays(record.createdon),
    // The grade lives on Response, which Wave F does not surface, so no outcome yet.
    latestOutcome: null,
    nextAction: NEXT_ACTION[status] ?? '',
  };
}
