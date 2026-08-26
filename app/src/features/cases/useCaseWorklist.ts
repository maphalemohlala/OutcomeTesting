import type { CaseStatus, Outcome, ReviewRoute } from '../../types/domain';

export interface CaseSummary {
  id: string;
  caseReference: string;
  route: ReviewRoute;
  status: CaseStatus;
  owner: string | null;
  ageInDays: number;
  latestOutcome: Outcome | null;
  nextAction: string;
}

export type WorklistState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; cases: CaseSummary[] };

/**
 * The Outcome Case table does not exist yet, so there is nothing to query.
 * Replace this with the generated Dataverse service behind a repository once
 * the schema is deployed. Never return sample rows from here.
 */
export function useCaseWorklist(): WorklistState {
  return {
    status: 'unavailable',
    reason: 'The Outcome Case table has not been created in this environment.',
  };
}
