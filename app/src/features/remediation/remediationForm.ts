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

/** One labelled cell of the paper form's answer grid. */
export interface FormCell {
  label: string;
  value: string | null;
}

/**
 * The paper form below the table, in the shape the document draws it (project owner,
 * 2026-09-10: a remediation should read like the review forms).
 *
 * Three blocks, not one flat list: the four answers laid out two to a row, the regraded
 * outcome with the note the paper prints beside it, and the two sign-offs as a table of
 * signatory and date. Grouped here rather than in either renderer so the portal and the
 * Code App draw the same document, and a test fails if one of them drifts.
 */
export interface RemediationFormLayout {
  /** The answer grid, as rows of two cells - the paper's own arrangement. */
  answers: FormCell[][];
  regrade: {
    label: string;
    /** The paper's parenthetical, which says whose signature this is. */
    note: string;
    outcome: string | null;
    on: string | null;
  };
  signoffs: {
    label: string;
    /** The paper's "(complete remedial task in IO)" instruction. */
    note: string;
    by: string | null;
    on: string | null;
  }[];
}

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
  /** The supervisor's decision and date, kept apart for the paper's sign-off table. */
  supervisorSignoffOn: string | null;
  adviserSignoffOn: string | null;
}

/**
 * The block arranged as the paper form draws it. Derived from the same values the flat
 * block carries, so there is one derivation and two arrangements of it.
 */
export function remediationFormLayout(block: RemediationFormBlock): RemediationFormLayout {
  return {
    answers: [
      [
        { label: 'Client contact required?', value: block.clientContactRequired },
        { label: 'Recheck required?', value: block.recheckRequired },
      ],
      [
        { label: 'Do the remedial actions change the advice?', value: block.changesAdvice },
        { label: 'All remedial actions checked and approved?', value: block.allApproved },
      ],
    ],
    regrade: {
      label: 'Regraded outcome',
      note: 'for potential harms / insufficient evidence — the supervisor signs this off',
      outcome: block.regradedOutcome,
      on: block.regradedOn,
    },
    signoffs: [
      {
        label: 'Supervisor sign-off',
        note: 'complete remedial task in IO',
        by: block.supervisorSignoff,
        on: block.supervisorSignoffOn,
      },
      {
        label: 'Adviser sign-off',
        note: 'complete remedial task in IO',
        by: block.adviserSignoff,
        on: block.adviserSignoffOn,
      },
    ],
  };
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
 * Which checks this case has finished with (AD-114).
 *
 * A Tax-then-AQS case remediates twice and the two are separate remediations: its Tax check
 * raises its own actions and its AQS check raises its own, each carrying the review it came
 * from. A check whose every action has been approved is settled - approved as a whole, which
 * is the rule SignoffProgressPlugin enforces server-side, so the case leaves Awaiting
 * Sign-off only once every action on the check has been decided.
 *
 * Grouped by the review's name rather than its id because that is what the row carries
 * (`triggeredBy`, from al_reviewinstanceidname) and a case has at most one review per
 * discipline, so the name identifies it here.
 *
 * An action with no check of its own is never settled away. Rows written before the review
 * link existed carry none, and collapsing those would lose work rather than tidy it.
 */
export function settledChecks(actions: RemediationActionRow[], signoffs: SignoffRow[]): string[] {
  const order: string[] = [];
  const live = new Set<string>();

  for (const action of actions) {
    const check = action.triggeredBy;
    if (!check) continue;

    if (!order.includes(check)) order.push(check);

    const latest = latestSignoff(action, signoffs);
    if (!latest || latest.decision !== APPROVED) live.add(check);
  }

  return order.filter((check) => !live.has(check));
}

/**
 * The actions still worth drawing: everything except the checks that are settled.
 *
 * Collapsing rather than showing them read-only is the project owner's call (2026-09-10) -
 * what stays on the form is the remediation in hand, and the case record keeps the rest.
 * When every check is settled this is empty, which the table renders as its own state
 * rather than as columns heading nothing.
 */
export function liveActions(
  actions: RemediationActionRow[],
  signoffs: SignoffRow[],
): RemediationActionRow[] {
  const settled = settledChecks(actions, signoffs);
  if (settled.length === 0) return actions;
  return actions.filter((action) => !action.triggeredBy || !settled.includes(action.triggeredBy));
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
    supervisorSignoffOn: supervisor?.signedOffOn ?? null,
    adviserSignoffOn: latestAdviser?.completedOn ?? null,
    // No Intelligent Office reference: the agreed remediation form does not carry one, so
    // it was removed from both platforms (project owner, 2026-09-10). The column and any
    // value already in it are left alone; nothing collects or shows it.
    adviserSignoff: latestAdviser
      ? [latestAdviser.assignedTo, latestAdviser.completedOn]
          .filter((part) => part !== null && part !== '')
          .join(', ')
      : null,
  };
}
