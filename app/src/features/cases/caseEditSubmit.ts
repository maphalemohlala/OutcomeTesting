/**
 * What the case edit modal says after running its commands (AD-040, BR-012).
 *
 * The modal's one Save button drives al_UpdateCaseDetails for the case fields, then one
 * al_AssignCase per check whose checker was changed — a Tax-then-AQS case can allocate
 * both in a single save. They are separate commands with separate audit events, so "it
 * worked" and "it failed" are not the only two answers: the fields can commit and an
 * allocation still be refused, or one check land and the other not. The wording for each
 * combination lives here rather than in the component so it can be tested directly, the
 * way remediationForm holds the remediation block's rules.
 *
 * Order is fixed by the caller: fields first, allocations second, because a successful
 * allocation moves the case to Assigned and bumps its row version — the other order would
 * make the field save fail its own concurrency check. Allocations are not attempted at all
 * when the fields were refused.
 */

/** What became of one command. */
export type CommandOutcome =
  | { kind: 'skipped' }
  | { kind: 'ok'; alreadyDone?: boolean }
  | { kind: 'failed'; message: string };

/** One check's allocation attempt, named so a refusal can say which check it was. */
export interface AllocationOutcome {
  /** "Tax", "AQS" — what the check is called on screen. */
  label: string;
  outcome: CommandOutcome;
}

export interface SubmitOutcome {
  /** Success line for the panel, or null when there is nothing to report. */
  notice: string | null;
  /** Failure line for inside the modal, or null. */
  error: string | null;
  /** Whether the modal should close. */
  close: boolean;
  /** Whether the case underneath should be re-read, because something did commit. */
  reload: boolean;
}

export function describeSubmit(
  fields: CommandOutcome,
  allocations: AllocationOutcome[] = [],
): SubmitOutcome {
  // A refused field save stops the sequence before any allocation runs, so nothing
  // committed and there is nothing to re-read.
  if (fields.kind === 'failed') {
    return { notice: null, error: fields.message, close: false, reload: false };
  }

  const attempted = allocations.filter((a) => a.outcome.kind !== 'skipped');
  const failures = attempted.filter((a) => a.outcome.kind === 'failed');
  const allocated = attempted.filter(
    (a) => a.outcome.kind === 'ok' && !a.outcome.alreadyDone,
  );

  const fieldsSaved = fields.kind === 'ok';

  if (failures.length > 0) {
    // Name the half that landed first. Reporting only the refusal would be untrue about
    // what did commit, and would invite a retry that re-sends it.
    const refusals = failures
      .map((a) => `${a.label}: ${stripStop((a.outcome as { message: string }).message)}`)
      .join('; ');

    const saved: string[] = [];
    if (fieldsSaved) saved.push('Case details were saved');
    if (allocated.length > 0) {
      saved.push(`${listOf(allocated.map((a) => a.label))} allocated`);
    }

    const error =
      saved.length > 0
        ? `${sentenceCase(saved.join(', and '))}, but ${failures.length === 1 ? 'one check' : 'some checks'} could not be allocated — ${refusals}. Nothing else has been changed.`
        : `${refusals}.`;

    return { notice: null, error, close: false, reload: fieldsSaved || allocated.length > 0 };
  }

  const parts: string[] = [];
  if (fieldsSaved) parts.push('Case details updated');
  if (allocated.length > 0) {
    parts.push(
      `${listOf(allocated.map((a) => a.label))} allocated — the checker will see it in their portal worklist`,
    );
  }

  if (parts.length > 0) {
    return { notice: `${sentenceCase(parts.join(', and '))}.`, error: null, close: true, reload: true };
  }

  // Everything that ran was already recorded, so the save was a no-op rather than a
  // failure. Say so plainly instead of claiming a change.
  if (attempted.length > 0) {
    return {
      notice: 'That allocation had already been recorded, so nothing was changed.',
      error: null,
      close: true,
      reload: true,
    };
  }

  // Neither ran. Validation should have caught this, so say nothing rather than claim a
  // save that did not happen.
  return { notice: null, error: null, close: false, reload: false };
}

/** "Tax", "Tax and AQS", "Tax, AQS and Recheck". */
function listOf(labels: string[]): string {
  if (labels.length === 1) return `${labels[0]} check`;
  const last = labels[labels.length - 1];
  return `${labels.slice(0, -1).join(', ')} and ${last} checks`;
}

/** One trailing full stop, so a refusal reads inside our own sentence. */
function stripStop(message: string): string {
  return message.trim().replace(/\.$/, '');
}

function sentenceCase(value: string): string {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

export interface SubmitFields {
  /** How many case fields the user actually changed. */
  changedCount: number;
  /** How many checks have a new checker chosen. */
  allocationCount: number;
}

/**
 * What the user must fix before the save runs. Empty when the form is ready.
 *
 * There is no "which check?" question any more: each check has its own checker field, so
 * choosing a person names the check by construction.
 */
export function validateSubmit(input: SubmitFields): string[] {
  if (input.changedCount === 0 && input.allocationCount === 0) {
    return ['Change at least one field, or choose a checker to allocate a check to.'];
  }

  return [];
}
