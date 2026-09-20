import { OUTCOMES, type Outcome } from '../../types/domain';
import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';
import {
  Al_remediationactionsal_actionstatus,
  type Al_remediationactions,
} from '../../generated/models/Al_remediationactionsModel';
import {
  REMEDIATION_THRESHOLD_WORKING_DAYS,
  remediationClock,
} from '../../lib/workingDays';
import {
  Al_signoffsal_signoffdecision,
  type Al_signoffs,
} from '../../generated/models/Al_signoffsModel';

/**
 * The BR-010 aggregation, kept apart from the hook that fetches for it.
 *
 * It lives here so it can be tested. useReports imports the generated Services, those drag
 * the Power Apps SDK in, and the SDK does not load under vitest - so everything in that
 * file was unreachable from a test. Two under-counting bugs survived in it until the
 * figures were read off the running page in DEV (2026-09-20): a null final outcome counted
 * as finalised and then matched no grade, and an action raised today fell outside every
 * ageing band. Both made the breakdowns disagree with their own headline tiles.
 */

export interface OutcomeVolume {
  outcome: Outcome;
  count: number;
}

export interface AgeingBucket {
  label: string;
  count: number;
}

export interface ReportData {
  /** BR-010 outcome volumes: effective grade is the final outcome, or the initial where none is set (BR-007). */
  outcomeTotal: number;
  finalisedCount: number;
  regradedCount: number;
  outcomeVolumes: OutcomeVolume[];
  /** BR-006 remediation ageing on actions that are not yet Completed. */
  openRemediation: number;
  overdueRemediation: number;
  remediationAgeing: AgeingBucket[];
  /** BR-008 accountability: T&C sign-off decisions. */
  signoffApproved: number;
  signoffRejected: number;
}

// Working-day bands, not calendar (OD-018). The second band ends on the BR-010
// ten-working-day threshold, so anything below the fold has breached it.
//
// The first band starts at NOUGHT, not one. An action raised this morning is nought
// working days old, and while the band started at one it matched no band at all: the
// action was counted as open in the tile above and then vanished from the breakdown
// below it (DEV, 2026-09-20). Every open action has to land in exactly one band, or the
// two halves of the same page disagree.
const AGEING_BANDS: { label: string; min: number; max: number }[] = [
  { label: '0–5 working days', min: 0, max: 5 },
  { label: `6–${REMEDIATION_THRESHOLD_WORKING_DAYS} working days`, min: 6, max: REMEDIATION_THRESHOLD_WORKING_DAYS },
  { label: '11–20 working days', min: 11, max: 20 },
  { label: 'Over 20 working days', min: 21, max: Infinity },
];

function isOverdue(dueDate: string | undefined): boolean {
  if (!dueDate) return false;
  const due = new Date(dueDate).getTime();
  if (Number.isNaN(due)) return false;
  return due < Date.now();
}

/**
 * Whether this case has actually been regraded.
 *
 * Dataverse leaves al_finaloutcome unset until a T&C Manager regrades, but the SDK hands
 * the property over as NULL rather than leaving it off, and the generated type says
 * `al_finaloutcome?:` - optional, never null. So `!== undefined` looked like the right
 * test, type-checked, and was wrong at runtime for every ungraded case.
 *
 * It cost twice over (DEV, 2026-09-20). The case counted as finalised when it was not, and
 * the grade lookup then ran against null, found no label, and dropped the case out of the
 * volumes entirely - so a case graded Pass with issues was reported as no grade at all.
 */
function hasFinalOutcome(record: Al_outcomes): boolean {
  return record.al_finaloutcome !== undefined && record.al_finaloutcome !== null;
}

function effectiveOutcome(record: Al_outcomes): Outcome | null {
  const label = hasFinalOutcome(record)
    ? Al_outcomesal_finaloutcome[record.al_finaloutcome as keyof typeof Al_outcomesal_finaloutcome]
    : Al_outcomesal_initialoutcome[record.al_initialoutcome];
  return (OUTCOMES as readonly string[]).includes(label) ? (label as Outcome) : null;
}

export function aggregate(
  outcomes: Al_outcomes[],
  actions: Al_remediationactions[],
  signoffs: Al_signoffs[],
): ReportData {
  const volumes = new Map<Outcome, number>();
  let finalisedCount = 0;
  let regradedCount = 0;

  for (const record of outcomes) {
    if (hasFinalOutcome(record)) finalisedCount += 1;
    if (record.al_regradedon) regradedCount += 1;
    const outcome = effectiveOutcome(record);
    if (outcome) volumes.set(outcome, (volumes.get(outcome) ?? 0) + 1);
  }

  const bands = AGEING_BANDS.map((band) => ({ label: band.label, count: 0 }));
  let openRemediation = 0;
  let overdueRemediation = 0;

  for (const record of actions) {
    const status =
      record.al_actionstatusname ?? Al_remediationactionsal_actionstatus[record.al_actionstatus];
    if (status === 'Completed') continue;

    openRemediation += 1;
    if (isOverdue(record.al_duedate)) overdueRemediation += 1;

    // The BR-010 clock is working days (OD-018). Calendar days were provisional and are
    // no longer used for remediation. The band is the period now running, because a
    // rejected sign-off restarts the clock and the previous period is preserved rather
    // than merged — banding on the merged age would put a freshly reworked action in the
    // oldest band on the strength of a round that is already closed.
    const age = remediationClock(record).current;
    const bandIndex = AGEING_BANDS.findIndex((band) => age >= band.min && age <= band.max);
    if (bandIndex >= 0) bands[bandIndex].count += 1;
  }

  let signoffApproved = 0;
  let signoffRejected = 0;
  for (const record of signoffs) {
    const decision =
      record.al_signoffdecisionname ?? Al_signoffsal_signoffdecision[record.al_signoffdecision];
    if (decision === 'Approved') signoffApproved += 1;
    if (decision === 'Rejected') signoffRejected += 1;
  }

  return {
    outcomeTotal: outcomes.length,
    finalisedCount,
    regradedCount,
    outcomeVolumes: OUTCOMES.map((outcome) => ({ outcome, count: volumes.get(outcome) ?? 0 })),
    openRemediation,
    overdueRemediation,
    remediationAgeing: bands,
    signoffApproved,
    signoffRejected,
  };
}
