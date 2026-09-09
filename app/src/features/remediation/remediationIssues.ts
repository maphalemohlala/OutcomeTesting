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

export interface RemediationIssues {
  /** One entry per item the checker marked down; empty when the description carries none. */
  issues: string[];
  /** What is left once the items are out: the checker's observation and why this was raised. */
  note: string | null;
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

    if (line === ISSUES_HEADING) {
      continue;
    }

    rest.push(line);
  }

  // The blank line between the observation and the standing sentence is a paragraph break
  // worth keeping; the gap the items left behind is not.
  const note = rest.join('\n').replace(/\n{3,}/g, '\n\n').trim();

  return { issues, note: note ? note : null };
}

/** Columns in the actions table, for the row a group's note spans. */
export const ACTION_COLUMNS = 11;

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
  return actions.map((action) => {
    const { issues, note } = splitIssues(action.description);
    // An action raised before the list existed, or one whose description is only the
    // standing sentence, still gets its row: the note is the issue it has to show.
    const lines = issues.length > 0 ? issues : [note ?? '—'];
    return {
      action,
      lines: lines.map((issue) => {
        number += 1;
        return { number, issue };
      }),
      note: issues.length > 0 ? note : null,
    };
  });
}
