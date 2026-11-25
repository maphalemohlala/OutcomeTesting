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
  Assigned: ['Review In Progress', 'Queued', 'No Check Required'],
  // Assigned: a manager reallocating a check the checker has already started (project
  // owner direction, 2026-09-10). al_AssignCase stamps the review instance back to
  // Assigned for whoever now holds it, and the case follows it there.
  'Review In Progress': ['Submitted', 'Queued', 'Assigned', 'No Check Required'],
  Submitted: ['Awaiting Remediation', 'Closed'],
  'Awaiting Remediation': ['Remediation In Progress'],
  // Closed: a remediation with nothing for the T&C Manager to verify (Pass with issues, or
  // a flagged Pass) closes on the adviser's last completion. The server's status guard
  // refuses it for any other case, so offering it here decides nothing.
  'Remediation In Progress': ['Awaiting Sign-off', 'Closed'],
  // Queued: OD-038 — a Tax check that raised remediation holds the case until the
  // remediation is approved, and the AQS check then follows from the shared queue.
  'Awaiting Sign-off': ['Awaiting Recheck', 'Awaiting Remediation', 'Queued'],
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

/**
 * Whether al_RegradeCase accepts a case at this status. Mirrors
 * RegradeCasePlugin.EnsureCaseIsAtTheRecheck: the final outcome is the recheck decision.
 */
export function mayRegradeAt(status: CaseStatus | null | undefined): boolean {
  return !status || status === 'Awaiting Recheck' || status === 'Closed';
}

/** BR-005. The only four outcomes the solution may record. */
export const OUTCOMES = [
  'Pass',
  'Pass with issues',
  'Insufficient evidence',
  'Potential harm',
] as const;

export type Outcome = (typeof OUTCOMES)[number];

/** BR-004. Tax always precedes AQS when both are required. */
export const REVIEW_ROUTES = ['Tax only', 'AQS only', 'Tax then AQS'] as const;

export type ReviewRoute = (typeof REVIEW_ROUTES)[number];

export const REVIEW_TYPES = ['Tax', 'AQS'] as const;

export type ReviewType = (typeof REVIEW_TYPES)[number];

/**
 * What `al_outcomecase.ownerid` is called wherever it is shown (item 4, 2026-09-19).
 *
 * A label change and nothing else: the column is the Dataverse record owner, and it reads as
 * the uploader because nothing in the solution ever reassigns it - every `ownerid` write
 * targets `al_reviewinstance` or `al_remediationaction`. The Dataverse display name was
 * changed to match, so advanced find and any model-driven view agree with the app.
 *
 * Deliberately NOT applied to the other "Owner" columns, which are different people: the
 * reviews table on a case shows the review's owner (the checker), and the remediation tables
 * show the action's owner (the adviser). Renaming those would be a lie rather than a fix.
 */
export const UPLOADED_BY_LABEL = 'Uploaded by';

/**
 * ServiceCaseSequentialRef from the upload: the reference people quote. Two tasks can share one
 * service case, so it is shown beside the TaskID (the case key), never instead of it
 * (client, 2026-11-24).
 */
export const SERVICE_CASE_REF_LABEL = 'Service case reference';
