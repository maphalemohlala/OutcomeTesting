/*
 * Domain vocabulary. Values trace to the approved requirements only:
 * lifecycle from the canonical lifecycle, outcomes from BR-005, routes from BR-004.
 * No Check Required is the OD-008/AD-036 bypass state. Do not add a value here without a requirement ID.
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
  'No Check Required',
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
  'No Check Required': 'closed',
};

export function stageTone(status: CaseStatus): StageTone {
  return STAGE_TONES[status];
}

/**
 * The canonical lifecycle, as the set of statuses each status may become. Mirrors the
 * server-side table in plugins/OutcomeTesting.Plugins/CaseLifecycle.cs, which is the
 * authoritative gate (AD-003); this exists so the status control offers only what the
 * command would accept, instead of all thirteen values on a mandatory field.
 *
 * Sources, per state: the spine is the canonical lifecycle above. Validation Failed
 * returns to allocation once intake data is corrected, or closes where work must restart
 * and the case is resubmitted as a new one (BR-002, AD-029). Awaiting Sign-off returns to
 * Awaiting Remediation when the T&C Manager rejects it "with notes" (BR-008). No Check
 * Required is the AD-036 bypass for cases that must not receive a grading outcome, so it
 * is reachable only while no grade exists. Closed and No Check Required are terminal:
 * reopening a closed outcome is the privileged AD-031 correction, not a details edit.
 */
export const CASE_STATUS_TRANSITIONS: Record<CaseStatus, readonly CaseStatus[]> = {
  Imported: ['Validation Failed', 'Ready for Allocation', 'No Check Required'],
  'Validation Failed': ['Ready for Allocation', 'Closed', 'No Check Required'],
  'Ready for Allocation': ['Queued', 'No Check Required'],
  Queued: ['Assigned', 'No Check Required'],
  Assigned: ['Review In Progress', 'No Check Required'],
  'Review In Progress': ['Submitted', 'No Check Required'],
  Submitted: ['Awaiting Remediation', 'Closed'],
  'Awaiting Remediation': ['Remediation In Progress'],
  'Remediation In Progress': ['Awaiting Sign-off'],
  'Awaiting Sign-off': ['Awaiting Recheck', 'Awaiting Remediation'],
  'Awaiting Recheck': ['Closed'],
  Closed: [],
  'No Check Required': [],
};

/**
 * The statuses a case may be moved to, including the one it already has — re-stating the
 * current status is not a transition. A case with no status recorded is not part-way
 * through the lifecycle, so every status is offered rather than none.
 */
export function nextStatuses(from: CaseStatus | null | undefined): readonly CaseStatus[] {
  if (!from) return CASE_STATUSES;
  return [from, ...CASE_STATUS_TRANSITIONS[from]];
}

/** Whether the lifecycle permits this move (BR-002, BR-008, AD-031, AD-036). */
export function canTransition(from: CaseStatus | null | undefined, to: CaseStatus): boolean {
  return nextStatuses(from).includes(to);
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

/**
 * OD-007 (resolved 2026-08-26, AD-031). The privileged actions that reopen or
 * rewrite a graded outcome. Each preserves the initial outcome as a new record
 * rather than editing it in place (BR-007).
 */
export const PRIVILEGED_OUTCOME_ACTIONS = ['Reopen', 'Override', 'Regrade'] as const;

export type PrivilegedOutcomeAction = (typeof PRIVILEGED_OUTCOME_ACTIONS)[number];

/** AD-020, AD-031. The elevated role that owns outcome corrections. */
export const OUTCOME_CORRECTION_ROLE = 'T&C Manager' as const;

/** AD-031. Platform-level escalation roles that may also perform a correction. */
export const OUTCOME_CORRECTION_ESCALATION_ROLES = [
  'Outcome Testing Manager',
  'Administrator',
] as const;

/** AD-031. Only the owning role or a platform escalation may correct an outcome. */
export function canCorrectOutcome(role: string): boolean {
  return (
    role === OUTCOME_CORRECTION_ROLE ||
    (OUTCOME_CORRECTION_ESCALATION_ROLES as readonly string[]).includes(role)
  );
}

/** BR-012, NFR-AUD-01, AD-031. A correction is rejected without a reason. */
export function isValidCorrectionReason(reason: string): boolean {
  return reason.trim().length > 0;
}

/** FR-023, BR-008. The decision a T&C Manager records when validating remediation. */
export const SIGNOFF_DECISIONS = ['Approved', 'Rejected'] as const;

export type SignoffDecision = (typeof SIGNOFF_DECISIONS)[number];

/**
 * The result of a client-side command guard. The authoritative check runs
 * server-side (AD-003); the guard stops an unauthorised or incomplete attempt
 * from ever being submitted and gives the UI a message to show.
 */
export interface CommandGuardResult {
  allowed: boolean;
  reason?: string;
}

/**
 * OD-007, AD-031 command guard for RegradeCase, mirrored client-side. Only the
 * T&C Manager (or a platform escalation) may regrade, and every regrade needs a
 * mandatory reason. The initial outcome is preserved as a new record (BR-007).
 */
export function guardRegradeCase(input: { role: string; reason: string }): CommandGuardResult {
  if (!canCorrectOutcome(input.role)) {
    return {
      allowed: false,
      reason: `Only ${OUTCOME_CORRECTION_ROLE} or a platform escalation may regrade a case.`,
    };
  }
  if (!isValidCorrectionReason(input.reason)) {
    return {
      allowed: false,
      reason: 'A regrade requires a mandatory reason for the audit event (BR-012, NFR-AUD-01).',
    };
  }
  return { allowed: true };
}

/**
 * BR-008, FR-023, AD-020 command guard for SignOffRemediation, mirrored
 * client-side. The T&C Manager validates Insufficient evidence and Potential
 * harm; a rejected sign-off must carry notes so the remediation returns with a
 * reason.
 */
export function guardSignOffRemediation(input: {
  role: string;
  decision: SignoffDecision;
  notes: string;
}): CommandGuardResult {
  if (!canCorrectOutcome(input.role)) {
    return {
      allowed: false,
      reason: `Only ${OUTCOME_CORRECTION_ROLE} or a platform escalation may sign off remediation.`,
    };
  }
  if (input.decision === 'Rejected' && input.notes.trim().length === 0) {
    return {
      allowed: false,
      reason: 'A rejected sign-off must record notes explaining the return (BR-008).',
    };
  }
  return { allowed: true };
}
