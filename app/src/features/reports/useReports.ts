import { useEffect, useState } from 'react';
import { OUTCOMES, type Outcome } from '../../types/domain';
import {
  Al_outcomesService,
  Al_remediationactionsService,
  Al_signoffsService,
} from '../../generated';
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
  Al_signoffsal_signoffdecision,
  type Al_signoffs,
} from '../../generated/models/Al_signoffsModel';

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

export type ReportState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; data: ReportData };

const AGEING_BANDS: { label: string; min: number; max: number }[] = [
  { label: '0–7 days', min: 0, max: 7 },
  { label: '8–14 days', min: 8, max: 14 },
  { label: '15–30 days', min: 15, max: 30 },
  { label: 'Over 30 days', min: 31, max: Infinity },
];

function ageInDays(value: string | undefined): number {
  if (!value) return 0;
  const then = new Date(value).getTime();
  if (Number.isNaN(then)) return 0;
  return Math.max(0, Math.floor((Date.now() - then) / 86_400_000));
}

function isOverdue(dueDate: string | undefined): boolean {
  if (!dueDate) return false;
  const due = new Date(dueDate).getTime();
  if (Number.isNaN(due)) return false;
  return due < Date.now();
}

function effectiveOutcome(record: Al_outcomes): Outcome | null {
  const label =
    record.al_finaloutcome !== undefined
      ? Al_outcomesal_finaloutcome[record.al_finaloutcome]
      : Al_outcomesal_initialoutcome[record.al_initialoutcome];
  return (OUTCOMES as readonly string[]).includes(label) ? (label as Outcome) : null;
}

function aggregate(
  outcomes: Al_outcomes[],
  actions: Al_remediationactions[],
  signoffs: Al_signoffs[],
): ReportData {
  const volumes = new Map<Outcome, number>();
  let finalisedCount = 0;
  let regradedCount = 0;

  for (const record of outcomes) {
    if (record.al_finaloutcome !== undefined) finalisedCount += 1;
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

    const age = ageInDays(record.createdon);
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

/**
 * Management information for BR-010: outcome volumes, remediation ageing and sign-off
 * accountability, read from live Dataverse (AD-034, no Power BI). Read-only aggregation;
 * row visibility is enforced by Dataverse security (BR-012), so the figures reflect only
 * the cases the signed-in user may see.
 */
export function useReports(): ReportState {
  const [state, setState] = useState<ReportState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_outcomesService.getAll({ top: 5000 }),
      Al_remediationactionsService.getAll({ top: 5000 }),
      Al_signoffsService.getAll({ top: 5000 }),
    ])
      .then(([outcomes, actions, signoffs]) => {
        if (cancelled) return;
        if (!outcomes.success || !actions.success || !signoffs.success) {
          setState({
            status: 'unavailable',
            reason: 'Management reporting could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          data: aggregate(outcomes.data, actions.data, signoffs.data),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'Management reporting could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, []);

  return state;
}
