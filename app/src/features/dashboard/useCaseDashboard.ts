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
import {
  Al_remediationactionsal_actionstatus,
  type Al_remediationactions,
} from '../../generated/models/Al_remediationactionsModel';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import { gradesByCase } from '../cases/caseWorklistMapping';
import { remediationClock } from '../../lib/workingDays';

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
  /** BR-005 grades on cases that have reached an outcome, for the PP-17 drill-down. */
  completedOutcomes: OutcomeCount[];
  completedTotal: number;
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
  | { status: 'ready'; data: DashboardData };

function ageInDays(createdOn: string | undefined): number {
  if (!createdOn) return 0;
  const created = new Date(createdOn).getTime();
  if (Number.isNaN(created)) return 0;
  return Math.max(0, Math.floor((Date.now() - created) / 86_400_000));
}

const AGEING_BANDS: { label: string; min: number; max: number }[] = [
  { label: '0–7 days', min: 0, max: 7 },
  { label: '8–14 days', min: 8, max: 14 },
  { label: '15–30 days', min: 15, max: 30 },
  { label: 'Over 30 days', min: 31, max: Infinity },
];

function isOverdue(dueDate: string | undefined): boolean {
  if (!dueDate) return false;
  const due = new Date(dueDate).getTime();
  return !Number.isNaN(due) && due < Date.now();
}

function aggregate(
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

  for (const record of records) {
    const status = Al_outcomecasesal_casestatus[record.al_casestatus] as CaseStatus;
    counts.set(status, (counts.get(status) ?? 0) + 1);

    if (status === 'Validation Failed') validationFailed += 1;
    if (status === 'Closed') continue;

    totalOpen += 1;
    if (!record._al_reviewrouteid_value) unrouted += 1;

    const age = ageInDays(record.createdon);
    if (age > oldestOpenDays) oldestOpenDays = age;

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

  let remediationOpen = 0;
  let remediationOverdue = 0;
  let remediationBreached = 0;
  let remediationCompleted = 0;

  for (const action of actions) {
    const status =
      action.al_actionstatusname ?? Al_remediationactionsal_actionstatus[action.al_actionstatus];
    if (status === 'Completed') {
      remediationCompleted += 1;
      continue;
    }
    remediationOpen += 1;
    if (isOverdue(action.al_duedate)) remediationOverdue += 1;
    // The current period, not the whole time in remediation: a rejected sign-off restarts
    // the clock (OD-018), and counting from the original start would report a reworked
    // action as breached before the adviser had had a day on it.
    if (remediationClock(action).breached) {
      remediationBreached += 1;
    }
  }

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
    completedOutcomes: OUTCOMES.map((outcome) => ({
      outcome,
      count: outcomeCounts.get(outcome) ?? 0,
    })),
    completedTotal,
    ungraded: records.length - completedTotal,
    remediationOpen,
    remediationOverdue,
    remediationBreached,
    remediationCompleted,
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
      Al_outcomecasesService.getAll({ top: 5000 }),
      Al_outcomesService.getAll({ top: 5000 }),
      Al_remediationactionsService.getAll({ top: 5000 }),
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
          data: aggregate(
            cases.data,
            outcomes.success ? outcomes.data : [],
            actions.success ? actions.data : [],
          ),
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
