import { useEffect, useState } from 'react';
import type { CaseStatus } from '../../types/domain';
import { CASE_STATUSES } from '../../types/domain';
import { Al_outcomecasesService } from '../../generated';
import {
  Al_outcomecasesal_casestatus,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';

export interface StatusCount {
  status: CaseStatus;
  count: number;
}

export interface AgeingBucket {
  label: string;
  count: number;
}

export interface DashboardData {
  totalOpen: number;
  validationFailed: number;
  unrouted: number;
  byStatus: StatusCount[];
  ageing: AgeingBucket[];
  oldestOpenDays: number;
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

function aggregate(records: Al_outcomecases[]): DashboardData {
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
    if (!record.al_reviewrouteidname) unrouted += 1;

    const age = ageInDays(record.createdon);
    if (age > oldestOpenDays) oldestOpenDays = age;

    const bandIndex = AGEING_BANDS.findIndex((band) => age >= band.min && age <= band.max);
    if (bandIndex >= 0) bands[bandIndex].count += 1;
  }

  const byStatus = CASE_STATUSES.map((status) => ({
    status,
    count: counts.get(status) ?? 0,
  })).filter((entry) => entry.count > 0);

  return { totalOpen, validationFailed, unrouted, byStatus, ageing: bands, oldestOpenDays };
}

/**
 * Aggregates the Outcome Case worklist into the dashboard's three questions: what is
 * waiting, what is ageing and what has failed validation. Only cases the signed-in user
 * may see are counted, because Dataverse enforces row visibility (BR-012).
 */
export function useCaseDashboard(): DashboardState {
  const [state, setState] = useState<DashboardState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Al_outcomecasesService.getAll({ top: 5000 })
      .then((result) => {
        if (cancelled) return;
        if (!result.success) {
          setState({
            status: 'unavailable',
            reason: 'The dashboard could not be loaded from Dataverse.',
          });
          return;
        }
        setState({ status: 'ready', data: aggregate(result.data) });
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
