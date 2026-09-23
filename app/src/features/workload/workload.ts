import { workingDaysBetween } from '../../lib/workingDays';

/**
 * A team's workload (AR-01: "monitor individual and overall Tax Specialist workloads").
 * Pure, over the reviews the manager's Dataverse share already limits to their team
 * (AD-218), so this filters nothing by team itself.
 */
export interface WorkloadReview {
  checker: string | null;
  status: string;
  startedOn: string | null;
  submittedOn: string | null;
  /** When the check was opened: the review's created date. */
  openedOn: string | null;
}

export interface CheckerLoad {
  checker: string;
  allocated: number;
  inProgress: number;
  completedLast30Days: number;
  oldestOpenWorkingDays: number | null;
}

export interface Workload {
  totals: { awaitingAllocation: number; allocated: number; inProgress: number; completedThisMonth: number };
  checkers: CheckerLoad[];
}

function day(value: string | null): Date | null {
  if (!value) return null;
  const parsed = new Date(`${value.slice(0, 10)}T12:00:00Z`);
  return Number.isNaN(parsed.getTime()) ? null : parsed;
}

export function summarise(reviews: WorkloadReview[], queued: number, now: Date): Workload {
  const today = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(), 12));
  const thirtyDaysAgo = new Date(today.getTime() - 30 * 24 * 60 * 60 * 1000);
  const monthStart = new Date(Date.UTC(today.getUTCFullYear(), today.getUTCMonth(), 1, 12));

  const byChecker = new Map<string, CheckerLoad>();
  const totals = { awaitingAllocation: queued, allocated: 0, inProgress: 0, completedThisMonth: 0 };

  for (const review of reviews) {
    const name = review.checker?.trim() || 'Unassigned';
    const load = byChecker.get(name) ?? {
      checker: name, allocated: 0, inProgress: 0, completedLast30Days: 0, oldestOpenWorkingDays: null,
    };

    if (review.status === 'Submitted') {
      const submitted = day(review.submittedOn);
      if (submitted && submitted >= thirtyDaysAgo) load.completedLast30Days += 1;
      if (submitted && submitted >= monthStart) totals.completedThisMonth += 1;
    } else {
      if (review.status === 'Review In Progress') {
        load.inProgress += 1;
        totals.inProgress += 1;
      } else {
        load.allocated += 1;
        totals.allocated += 1;
      }

      const opened = day(review.openedOn);
      if (opened) {
        const age = workingDaysBetween(opened, today);
        load.oldestOpenWorkingDays = Math.max(load.oldestOpenWorkingDays ?? 0, age);
      }
    }

    byChecker.set(name, load);
  }

  const checkers = [...byChecker.values()].sort(
    (a, b) => b.allocated + b.inProgress - (a.allocated + a.inProgress) || a.checker.localeCompare(b.checker),
  );
  return { totals, checkers };
}
