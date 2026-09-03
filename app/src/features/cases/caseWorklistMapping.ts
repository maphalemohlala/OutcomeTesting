import type { CaseStatus, Outcome, ReviewRoute } from '../../types/domain';
import { OUTCOMES, REVIEW_ROUTES } from '../../types/domain';
import {
  Al_outcomecasesal_casestatus,
  Al_outcomecasesal_casetype,
  Al_outcomecasesal_preorpostcheck,
  Al_outcomecasesal_priority,
  Al_outcomecasesal_productsolutiontype,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';
import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';
import { choiceLabel } from './choiceLabel';
import { lookupLabel } from './lookupLabel';

/**
 * Pure record-to-row mapping, kept free of the generated services so it stays unit
 * testable (the Power Apps data SDK cannot be imported under vitest).
 */

/** The BR-007 pair for one case, reduced to what a list row and an export need. */
export interface CaseOutcomeGrades {
  initial: Outcome | null;
  final: Outcome | null;
  finalisedOn: string | null;
  regradedOn: string | null;
}

export interface CaseSummary {
  id: string;
  caseReference: string;
  route: ReviewRoute | null;
  status: CaseStatus;
  owner: string | null;
  priority: string | null;
  createdOn: string | null;
  ageInDays: number;
  /** Final outcome, or the initial where none is set yet (BR-007). */
  latestOutcome: Outcome | null;
  initialOutcome: Outcome | null;
  finalOutcome: Outcome | null;
  finalisedOn: string | null;
  nextAction: string;
  client: string | null;
  adviser: string | null;
  adviserCode: string | null;
  paraplanner: string | null;
  paraplannerCode: string | null;
  checker: string | null;
  caseType: string | null;
  productSolutionType: string | null;
  products: string | null;
  adviceDate: string | null;
  checkDate: string | null;
  preOrPostCheck: string | null;
  dueDate: string | null;
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

function toOutcome(label: string | undefined): Outcome | null {
  return label && (OUTCOMES as readonly string[]).includes(label) ? (label as Outcome) : null;
}

/**
 * Reduces every Outcome row to one per case. Where a case carries more than one — a
 * Tax-then-AQS route records one per review instance — the most recently created wins,
 * because that is the grade the case currently stands on.
 */
export function gradesByCase(outcomes: Al_outcomes[]): Map<string, CaseOutcomeGrades> {
  const latest = new Map<string, { createdOn: number; grades: CaseOutcomeGrades }>();

  for (const record of outcomes) {
    const caseId = record._al_outcomecaseid_value;
    if (!caseId) continue;

    const createdOn = record.createdon ? new Date(record.createdon).getTime() : 0;
    const existing = latest.get(caseId);
    if (existing && existing.createdOn >= createdOn) continue;

    latest.set(caseId, {
      createdOn: Number.isNaN(createdOn) ? 0 : createdOn,
      grades: {
        initial: toOutcome(
          record.al_initialoutcomename ?? Al_outcomesal_initialoutcome[record.al_initialoutcome],
        ),
        final:
          record.al_finaloutcome === undefined
            ? null
            : toOutcome(record.al_finaloutcomename ?? Al_outcomesal_finaloutcome[record.al_finaloutcome]),
        finalisedOn: record.al_finalisedon ?? null,
        regradedOn: record.al_regradedon ?? null,
      },
    });
  }

  return new Map([...latest].map(([caseId, entry]) => [caseId, entry.grades]));
}

export function toSummary(record: Al_outcomecases, grades?: CaseOutcomeGrades): CaseSummary {
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
    latestOutcome: grades?.final ?? grades?.initial ?? null,
    initialOutcome: grades?.initial ?? null,
    finalOutcome: grades?.final ?? null,
    finalisedOn: grades?.finalisedOn ?? null,
    nextAction: NEXT_ACTION[status] ?? '',
    client: record.al_clientname ?? null,
    adviser: record.al_advisername ?? null,
    adviserCode: record.al_advisercode ?? null,
    paraplanner: record.al_paraplanner ?? null,
    paraplannerCode: record.al_paraplannercode ?? null,
    checker: record.al_checkername ?? null,
    caseType: choiceLabel(Al_outcomecasesal_casetype, record.al_casetype, record.al_casetypename),
    productSolutionType: choiceLabel(
      Al_outcomecasesal_productsolutiontype,
      record.al_productsolutiontype,
      record.al_productsolutiontypename,
    ),
    products: record.al_products ?? null,
    adviceDate: record.al_advicedate ?? null,
    checkDate: record.al_checkdate ?? null,
    preOrPostCheck: choiceLabel(
      Al_outcomecasesal_preorpostcheck,
      record.al_preorpostcheck,
      record.al_preorpostcheckname,
    ),
    dueDate: record.al_duedate ?? null,
  };
}
