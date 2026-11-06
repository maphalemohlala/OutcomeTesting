import { useEffect, useState } from 'react';
import type { CaseStatus, Outcome } from '../../types/domain';
import { CASE_STATUSES, OUTCOMES } from '../../types/domain';
import {
  Al_outcomecasesService,
  Al_outcomesService,
  Al_remediationactionsService,
} from '../../generated';
import {
  Al_outcomecasesal_casestatus,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import { ACTION_COUNT_COLUMNS } from '../cases/caseRemediation';
import {
  CASE_SUMMARY_COLUMNS,
  OUTCOME_GRADE_COLUMNS,
  ageInDays,
  gradesByCase,
} from '../cases/caseWorklistMapping';
import { remediationTotals } from './remediationTotals';
import { readAll } from '../../services/readAll';

export interface StatusCount {
  status: CaseStatus;
  count: number;
}

export interface AgeingBucket {
  label: string;
  count: number;
}

export interface OutcomeCount {
  outcome: Outcome;
  count: number;
}

export interface DashboardData {
  totalOpen: number;
  validationFailed: number;
  unrouted: number;
  byStatus: StatusCount[];
  ageing: AgeingBucket[];
  oldestOpenDays: number;
  /** The case that age belongs to, so the card can open the case it counted. */
  oldestOpenCaseId: string | null;
  /** BR-005 grades on cases that have reached an outcome, for the PP-17 drill-down. */
  completedOutcomes: OutcomeCount[];
  completedTotal: number;
  /** Every case in view, open or closed. */
  totalCases: number;
  ungraded: number;
  /** BR-006 remediation: what is open, and what has passed the BR-010 threshold. */
  remediationOpen: number;
  remediationOverdue: number;
  remediationBreached: number;
  remediationCompleted: number;
}

export type DashboardState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | {
      status: 'ready';
      /**
       * The rows, not the totals: the page filters the cases and narrows the rest to them
       * before counting (reportFilters), so the counting runs once per filter change.
       */
      cases: Al_outcomecases[];
      outcomes: Al_outcomes[];
      actions: Al_remediationactions[];
    };

const AGEING_BANDS: { label: string; min: number; max: number }[] = [
  { label: '0–7 days', min: 0, max: 7 },
  { label: '8–14 days', min: 8, max: 14 },
  { label: '15–30 days', min: 15, max: 30 },
  { label: 'Over 30 days', min: 31, max: Infinity },
];

export function aggregate(
  records: Al_outcomecases[],
  outcomes: Al_outcomes[],
  actions: Al_remediationactions[],
): DashboardData {
  const counts = new Map<CaseStatus, number>();
  const bands = AGEING_BANDS.map((band) => ({ label: band.label, count: 0 }));
  let totalOpen = 0;
  let validationFailed = 0;
  let unrouted = 0;
  let oldestOpenDays = 0;
  let oldestOpenCaseId: string | null = null;

  for (const record of records) {
    const status = Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus;
    counts.set(status, (counts.get(status) ?? 0) + 1);

    if (status === 'Validation Failed') validationFailed += 1;
    if (status === 'Closed' || status === 'No Check Required') continue;

    totalOpen += 1;
    if (!record._al_reviewrouteid_value) unrouted += 1;

    const age = ageInDays(record.createdon);
    if (age > oldestOpenDays || oldestOpenCaseId === null) {
      oldestOpenDays = age;
      oldestOpenCaseId = record.al_outcomecaseid;
    }

    const bandIndex = AGEING_BANDS.findIndex((band) => age >= band.min && age <= band.max);
    if (bandIndex >= 0) bands[bandIndex].count += 1;
  }

  // Counted per case rather than per outcome row, so a card and the worklist it drills
  // into show the same number even on a Tax-then-AQS route that records two outcomes.
  const grades = gradesByCase(outcomes);
  const outcomeCounts = new Map<Outcome, number>();
  for (const record of records) {
    const grade = grades.get(record.al_outcomecaseid);
    const effective = grade?.final ?? grade?.initial ?? null;
    if (effective) outcomeCounts.set(effective, (outcomeCounts.get(effective) ?? 0) + 1);
  }
  const completedTotal = [...outcomeCounts.values()].reduce((total, count) => total + count, 0);

  // Per remediation, not per action - the same correction the outcome counts above already
  // carry. remediationTotals holds the rule and the reasoning, and is tested there; this
  // module cannot be, because it imports the generated services.
  const remediation = remediationTotals(actions);

  const byStatus = CASE_STATUSES.map((status) => ({
    status,
    count: counts.get(status) ?? 0,
  })).filter((entry) => entry.count > 0);

  return {
    totalOpen,
    validationFailed,
    unrouted,
    byStatus,
    ageing: bands,
    oldestOpenDays,
    oldestOpenCaseId,
    completedOutcomes: OUTCOMES.map((outcome) => ({
      outcome,
      count: outcomeCounts.get(outcome) ?? 0,
    })),
    completedTotal,
    totalCases: records.length,
    // A Tax-only case has no outcome row but has been graded, on the Tax scale.
    ungraded: records.filter(
      (record) =>
        !grades.get(record.al_outcomecaseid)?.final &&
        !grades.get(record.al_outcomecaseid)?.initial &&
        record.al_taxoutcome == null,
    ).length,
    remediationOpen: remediation.open,
    remediationOverdue: remediation.overdue,
    remediationBreached: remediation.breached,
    remediationCompleted: remediation.completed,
  };
}

/**
 * Aggregates the Outcome Case worklist into the dashboard's questions: what is waiting,
 * what is ageing, what has failed validation, what has been graded (BR-005) and what sits
 * in remediation (BR-006, BR-010). Only cases the signed-in user may see are counted,
 * because Dataverse enforces row visibility (BR-012).
 */
export function useCaseDashboard(): DashboardState {
  const [state, setState] = useState<DashboardState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      readAll((o) => Al_outcomecasesService.getAll(o), { orderBy: ['al_outcomecaseid asc'], select: CASE_SUMMARY_COLUMNS }),
      readAll((o) => Al_outcomesService.getAll(o), { orderBy: ['al_outcomeid asc'], select: OUTCOME_GRADE_COLUMNS }),
      readAll((o) => Al_remediationactionsService.getAll(o), { orderBy: ['al_remediationactionid asc'], select: ACTION_COUNT_COLUMNS }),
    ])
      .then(([cases, outcomes, actions]) => {
        if (cancelled) return;
        if (!cases.success) {
          setState({
            status: 'unavailable',
            reason: 'The dashboard could not be loaded from Dataverse.',
          });
          return;
        }
        // Outcome and remediation reads degrade to zero rather than blanking the whole
        // dashboard, because a caller may hold case read without holding either.
        setState({
          status: 'ready',
          cases: cases.data,
          outcomes: outcomes.success ? outcomes.data : [],
          actions: actions.success ? actions.data : [],
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'The dashboard could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
