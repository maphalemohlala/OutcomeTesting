/*
 * Domain vocabulary. Values trace to the approved requirements only:
 * lifecycle from the canonical lifecycle, outcomes from BR-005, routes from BR-004.
 * Do not add a value here without a requirement ID.
 */

export const CASE_STATUSES = [
  'Imported',
  'Validation Failed',
  'Ready for Allocation',
  'Queued',
  'Assigned',
  'Review In Progress',
  'Submitted',
  'Awaiting Remediation',
  'Remediation In Progress',
  'Awaiting Sign-off',
  'Awaiting Recheck',
  'Closed',
] as const;

export type CaseStatus = (typeof CASE_STATUSES)[number];

export type StageTone = 'waiting' | 'active' | 'blocked' | 'closed';

const STAGE_TONES: Record<CaseStatus, StageTone> = {
  Imported: 'waiting',
  'Validation Failed': 'blocked',
  'Ready for Allocation': 'waiting',
  Queued: 'waiting',
  Assigned: 'active',
  'Review In Progress': 'active',
  Submitted: 'active',
  'Awaiting Remediation': 'blocked',
  'Remediation In Progress': 'active',
  'Awaiting Sign-off': 'waiting',
  'Awaiting Recheck': 'waiting',
  Closed: 'closed',
};

export function stageTone(status: CaseStatus): StageTone {
  return STAGE_TONES[status];
}

/** BR-005. The only four outcomes the solution may record. */
export const OUTCOMES = [
  'Pass',
  'Pass with issues',
  'Insufficient evidence',
  'Potential harm',
] as const;

export type Outcome = (typeof OUTCOMES)[number];

/** BR-006: every non-pass outcome requires remediation. */
export function requiresRemediation(outcome: Outcome): boolean {
  return outcome !== 'Pass';
}

/** BR-004. Tax always precedes AQS when both are required. */
export const REVIEW_ROUTES = ['Tax only', 'AQS only', 'Tax then AQS'] as const;

export type ReviewRoute = (typeof REVIEW_ROUTES)[number];

export const REVIEW_TYPES = ['Tax', 'AQS'] as const;

export type ReviewType = (typeof REVIEW_TYPES)[number];
