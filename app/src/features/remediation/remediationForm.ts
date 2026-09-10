/**
 * The last block of the agreed remediation form, and the sign-off cell beside each action.
 *
 * The portal draws this block under the actions table on both its remediation page and the
 * case record; the Code App drew the same information as four extra columns on every row
 * and two tables underneath. Those were not the form: the three answers are the adviser's
 * and there is one set of them per case, so a column repeated them down the table, and the
 * regrade and the supervisor's decision are the case's rather than any one action's.
 * Mirrored here instead (project owner, 2026-09-10), from the same rows and by the same
 * rules the Liquid uses, so the two cannot disagree.
 *
 * Pure, and apart from the page for the reason `remediationIssues` is: what a reader is
 * told about sign-off is a rule, and a rule belongs where it can be tested.
 */

import type { OutcomeRow, RemediationActionRow, SignoffRow } from './remediationMapping';

/** al_signoff.al_signoffdecision. Same values SubmitReviewPlugin and the Liquid use. */
const APPROVED = 'Approved';
const REJECTED = 'Rejected';

export interface RemediationFormBlock {
  clientContactRequired: string | null;
  recheckRequired: string | null;
  changesAdvice: string | null;
  /** Yes when every action's latest decision is Approved, No when any is Rejected. */
  allApproved: string | null;
  regradedOutcome: string | null;
  regradedOn: string | null;
  supervisorSignoff: string | null;
  adviserSignoff: string | null;
}

/**
 * The sign-offs against one action, most recent first.
 *
 * Ordered here rather than trusted from the fetch: the page reads the case's sign-offs in
 * creation order, and "latest" has to mean the latest decision on this action.
 */
function signoffsFor(action: RemediationActionRow, signoffs: SignoffRow[]): SignoffRow[] {
  return signoffs
    .filter((signoff) => signoff.remediationActionId === action.id)
    .slice()
    .sort((a, b) => (b.signedOffOn ?? '').localeCompare(a.signedOffOn ?? ''));
}

/** The most recent supervisor decision on an action, or null when it has none. */
export function latestSignoff(
  action: RemediationActionRow,
  signoffs: SignoffRow[],
): SignoffRow | null {
  const own = signoffsFor(action, signoffs);
  return own.length > 0 ? own[0] : null;
}

/**
 * What the Sign-off column says for one action.
 *
 * Three states, as the portal draws them: the supervisor has decided; the adviser has
 * completed and the supervisor has not; neither. The middle one is the reason this is not
 * just the decision label - an action sitting with the T&C Manager reads as blank
 * otherwise, and that is the state someone is most likely to be looking for.
 */
export function signoffCell(action: RemediationActionRow, signoffs: SignoffRow[]): string {
  const latest = latestSignoff(action, signoffs);
  if (latest) {
    return latest.signedOffOn ? `${latest.decision}, ${latest.signedOffOn}` : latest.decision;
  }

  if (action.completedOn) {
    return `Adviser completed ${action.completedOn}; awaiting supervisor`;
  }

  return '—';
}

/**
 * The form's last block, read off the same rows the table draws.
 *
 * The three answers come from the first action carrying any of them: they are answered once
 * for the case, and the plug-in writes them to whichever action the adviser answered on.
 * "All remedial actions checked and approved" is every action's latest decision being
 * Approved - not merely the absence of a rejection - so an action nobody has decided on
 * yet leaves it unanswered rather than passed.
 */
export function remediationForm(
  actions: RemediationActionRow[],
  outcomes: OutcomeRow[],
  signoffs: SignoffRow[],
): RemediationFormBlock {
  const source =
    actions.find(
      (action) =>
        action.clientContactRequired !== null ||
        action.recheckRequired !== null ||
        action.changesAdvice !== null,
    ) ?? null;

  let allApproved = actions.length > 0;
  let anyRejected = false;
  for (const action of actions) {
    const latest = latestSignoff(action, signoffs);
    if (!latest) {
      allApproved = false;
      continue;
    }
    if (latest.decision === REJECTED) {
      anyRejected = true;
      allApproved = false;
    } else if (latest.decision !== APPROVED) {
      allApproved = false;
    }
  }

  // The case's outcome, which is the one a regrade writes to. Read as the most recent, the
  // rows arriving in creation order.
  const outcome = outcomes.length > 0 ? outcomes[outcomes.length - 1] : null;

  // The most recently completed action carries the adviser's sign-off. Compared on the raw
  // completion rather than the row's display date, "22 Sep 2026" not being a value that
  // sorts - which is the same reason the row carries both.
  let latestAdviser: RemediationActionRow | null = null;
  for (const action of actions) {
    if (!action.completedOnRaw) continue;
    if (latestAdviser === null || action.completedOnRaw > (latestAdviser.completedOnRaw ?? '')) {
      latestAdviser = action;
    }
  }

  const supervisor = signoffs
    .slice()
    .sort((a, b) => (b.signedOffOn ?? '').localeCompare(a.signedOffOn ?? ''))[0];

  return {
    clientContactRequired: source?.clientContactRequired ?? null,
    recheckRequired: source?.recheckRequired ?? null,
    changesAdvice: source?.changesAdvice ?? null,
    allApproved: allApproved ? 'Yes' : anyRejected ? 'No' : null,
    regradedOutcome: outcome?.finalOutcome ?? null,
    regradedOn: outcome?.regradedOn ?? outcome?.finalisedOn ?? null,
    supervisorSignoff: supervisor
      ? [supervisor.decision, supervisor.signedOffBy, supervisor.signedOffOn]
          .filter((part) => part !== null && part !== undefined && part !== '')
          .join(', ')
      : null,
    adviserSignoff: latestAdviser
      ? [latestAdviser.assignedTo, latestAdviser.completedOn]
          .filter((part) => part !== null && part !== '')
          .join(', ') +
        (latestAdviser.evidenceReference ? ` (IO ${latestAdviser.evidenceReference})` : '')
      : null,
  };
}
