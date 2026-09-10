/**
 * Splitting a remediation action's description into the items it was built from.
 *
 * `Remediation.Describe` (plugins/OutcomeTesting.Plugins/Remediation.cs) writes one
 * `al_description`: a heading, one "- item" line per thing the checker marked down, then
 * the checker's observation and the standing sentence. The agreed form numbers the issues
 * one to a row, so the items are split back out here.
 *
 * Only the items are drawn. The heading, the observation and the standing sentence are all
 * read past (project owner, 2026-09-10): the sentence said the same thing under every row
 * of every case, and what it says about why the action was raised is the Outcome above the
 * table. All of it stays in `al_description`, which is the record Dataverse keeps.
 *
 * The two portal templates carry the same split in Liquid.
 */

import type { RemediationActionRow } from './remediationMapping';

/**
 * How `Remediation.Describe` opens the bracket holding the outcome: "...when the review was
 * submitted (Tax check: Insufficient evidence)." The outcome is not one of the numbered
 * items - it is the result every item is a reason for - so it is read back out here and
 * shown as the remediation's Outcome instead (project owner, 2026-09-10).
 */
const OUTCOME_MARKER = 'submitted (';

export interface RemediationIssues {
  /** One entry per item the checker marked down; empty when the description carries none. */
  issues: string[];
  /** The outcome the remediation was raised for; null when the description names none. */
  outcome: string | null;
}

/**
 * The outcome out of the description's standing sentence.
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

/**
 * The items and the outcome, out of one description.
 *
 * Only the "- item" lines are kept. The heading above them, the checker's observation and
 * the standing sentence are all read past: none of the three is drawn any more (project
 * owner, 2026-09-10), and what the description says about why the action was raised is the
 * Outcome above the table.
 */
export function splitIssues(description: string | null | undefined): RemediationIssues {
  const issues: string[] = [];

  for (const raw of (description ?? '').split(/\r?\n/)) {
    const line = raw.trim();

    if (line.startsWith('- ')) {
      const item = line.slice(2).trim();
      if (item) {
        issues.push(item);
      }
      continue;
    }

    // Everything else is the checker's observation, the heading or the standing sentence.
    // None of it is drawn (project owner, 2026-09-10): the observation went with the rest
    // when the table was cut back to the issues themselves, and what the description says
    // about why it was raised is the Outcome above the table.
  }

  return { issues, outcome: outcomeIn(description ?? '') };
}

export interface IssueLine {
  number: number;
  issue: string;
}

export interface ActionGroup {
  action: RemediationActionRow;
  lines: IssueLine[];
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

  return actions.map((action) => {
    const { issues } = splitIssues(action.description);
    // An action raised before the list existed, or one dropoutcomeactions stripped back to
    // its standing sentence, still gets its row - there is simply nothing to name in it.
    const lines = issues.length > 0 ? issues : ['—'];

    return {
      action,
      lines: lines.map((issue) => {
        number += 1;
        return { number, issue };
      }),
    };
  });
}
