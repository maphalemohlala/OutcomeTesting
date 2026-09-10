/**
 * Splitting a remediation action's description back into the items it was built from.
 *
 * `Remediation.Describe` (plugins/OutcomeTesting.Plugins/Remediation.cs) writes one
 * `al_description`: a heading, one "- item" line per thing the checker marked down, then
 * the checker's own observation and the standing sentence. Rendered as written, all of
 * that lands in a single "Issue / fail reason" cell, which is not the form the project
 * owner signed off - the paper form numbers the issues, one to a row.
 *
 * The split is done here rather than by raising an action per item: the action is the unit
 * the BR-010 clock, the adviser's response and the T&C sign-off all hang off, and one
 * action per issue would move the case to Awaiting Sign-off on the first completion.
 * The two portal templates carry the same split in Liquid.
 */

import type { RemediationActionRow } from './remediationMapping';

/** The heading `Remediation.Describe` puts above the items. Dropped, not shown as a row. */
const ISSUES_HEADING = 'Issues found on the check:';

/**
 * How `Remediation.Describe` opens the bracket holding the outcome: "...when the review was
 * submitted (Tax check: Insufficient evidence)." The outcome is not one of the numbered
 * items - it is the result every item is a reason for - so it is read back out here and
 * shown as the remediation's Outcome instead (project owner, 2026-09-10).
 */
const OUTCOME_MARKER = 'submitted (';

/**
 * The sentence `Remediation.Describe` ends every description with: "Raised automatically
 * when the review was submitted (<outcome>). Review the file and record what you have put
 * right."
 *
 * Not shown (project owner, 2026-09-10). It said the same thing under every row of every
 * case, and both halves of it are now on the page in their own right - the outcome as the
 * remediation's Outcome, and what to do about it as the row the adviser answers. What is
 * left of a note is the checker's own words, which are particular to the case.
 *
 * It stays in `al_description`, which is the record Dataverse keeps and the only place the
 * outcome is written down; this drops it on the way to the screen, not out of the data.
 */
const STANDING_SENTENCE = 'Raised automatically when the review was submitted';

/**
 * The second half of it. `Remediation.Describe` writes the whole sentence on one line, but
 * a description that has been through a renderer, an export or a hand edit can carry the
 * break, and half a sentence left under the rows is worse than either whole or gone.
 */
const STANDING_TAIL = 'Review the file and record what you have put right';

export interface RemediationIssues {
  /** One entry per item the checker marked down; empty when the description carries none. */
  issues: string[];
  /** What is left once the items are out: the checker's observation and why this was raised. */
  note: string | null;
  /** The outcome the remediation was raised for; null when the description names none. */
  outcome: string | null;
}

/**
 * The outcome out of the description's standing sentence.
 *
 * Read from the description rather than from the note, because the note no longer carries
 * the sentence: it is dropped on the way to the screen and the outcome would go with it.
 *
 * The marker has to be present before the brackets mean anything: a description with no
 * reason behind it still carries the sentence, and a checker's observation is free text
 * that may well have brackets of its own. Once it is present, the outcome is the last
 * bracket the description opens - the observation is written above the sentence, never
 * below it - which is the one rule the two portal templates can also follow, Liquid having
 * no multi-character `split` on this site.
 */
function outcomeIn(description: string): string | null {
  if (!description.includes(OUTCOME_MARKER)) {
    return null;
  }

  const open = description.lastIndexOf('(');
  const close = description.indexOf(')', open);
  if (close < 0) {
    return null;
  }

  const outcome = description.slice(open + 1, close).trim();
  return outcome ? outcome : null;
}

export function splitIssues(description: string | null | undefined): RemediationIssues {
  const issues: string[] = [];
  const rest: string[] = [];

  for (const raw of (description ?? '').split(/\r?\n/)) {
    const line = raw.trim();

    if (line.startsWith('- ')) {
      const item = line.slice(2).trim();
      if (item) {
        issues.push(item);
      }
      continue;
    }

    if (
      line === ISSUES_HEADING ||
      line.startsWith(STANDING_SENTENCE) ||
      line.startsWith(STANDING_TAIL)
    ) {
      continue;
    }

    rest.push(line);
  }

  // The blank line between the observation and the standing sentence is a paragraph break
  // worth keeping; the gap the items left behind is not.
  const note = rest.join('\n').replace(/\n{3,}/g, '\n\n').trim();

  return { issues, note: note ? note : null, outcome: outcomeIn(description ?? '') };
}

/**
 * Columns in the actions table, for the row a group's note spans.
 *
 * No., issue, remedial action, owner, target date, status, age, sign-off, and the column
 * the Mark complete button sits in. The four that carried the adviser's answers are gone:
 * they are the form's last block now, drawn once under the table as the portal draws it.
 */
export const ACTION_COLUMNS = 9;

export interface IssueLine {
  number: number;
  issue: string;
}

export interface ActionGroup {
  action: RemediationActionRow;
  lines: IssueLine[];
  note: string | null;
}

/**
 * The outcome the actions were raised for, or null when none of them names one.
 *
 * Read across the set rather than off the first row so a case whose Tax and AQS reviews
 * both raised remediation does not report only the earlier of the two. They are joined in
 * the order the actions come in, each shown once.
 */
export function outcomeOf(actions: RemediationActionRow[]): string | null {
  const seen: string[] = [];
  for (const action of actions) {
    const { outcome } = splitIssues(action.description);
    if (outcome !== null && !seen.includes(outcome)) {
      seen.push(outcome);
    }
  }

  return seen.length > 0 ? seen.join('; ') : null;
}

/**
 * One line per thing the checker marked down, numbered straight through the table.
 *
 * The action's description carries the whole list (Remediation.Describe), and the agreed
 * form numbers those items one to a row rather than showing them as a paragraph in a
 * single cell. The action is still one row of data - its remedial action, owner, target
 * date and sign-off span its issues - so the numbering runs across actions, as it does on
 * the paper form.
 */
export function groupIssues(actions: RemediationActionRow[]): ActionGroup[] {
  let number = 0;
  let shownNote: string | null = null;

  return actions.map((action) => {
    const { issues, note } = splitIssues(action.description);
    // An action raised before the list existed, or one whose description is only the
    // standing sentence, still gets its row: the note is the issue it has to show.
    const lines = issues.length > 0 ? issues : [note ?? '—'];

    // A review raises one action per item, and every one of them carries the same
    // provenance - the checker's observation and why the remediation was raised. Each
    // record keeps it so it stands alone in Dataverse; the table shows it once per run of
    // actions that share it, rather than under every row.
    const carried = issues.length > 0 ? note : null;
    const repeated = carried !== null && carried === shownNote;
    if (carried !== null) {
      shownNote = carried;
    }

    return {
      action,
      lines: lines.map((issue) => {
        number += 1;
        return { number, issue };
      }),
      note: repeated ? null : carried,
    };
  });
}
