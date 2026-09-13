import { Al_remediationactionsal_actionstatus } from '../../generated/models/Al_remediationactionsModel';
import { remediationClock } from '../../lib/workingDays';

/**
 * The dashboard's remediation counts, per remediation rather than per action.
 *
 * Kept out of useCaseDashboard so it can be unit tested: that module imports the generated
 * services, and the Power Apps data SDK cannot be imported under vitest. Same split as
 * dashboardShares, and the same reason caseDetailMapping gives.
 */

/** Only what the counting reads, so a test needs no generated model. */
export interface RemediationCountSource {
  al_remediationactionid: string;
  al_actionstatus: number;
  al_actionstatusname?: string;
  al_duedate?: string;
  al_completedon?: string;
  al_clockstartedon?: string;
  createdon?: string;
  _al_reviewinstanceid_value?: string;
}

export interface RemediationTotals {
  open: number;
  overdue: number;
  breached: number;
  completed: number;
}

function isOverdue(due: string | undefined): boolean {
  if (!due) return false;
  const at = new Date(due).getTime();
  return !Number.isNaN(at) && at < Date.now();
}

/**
 * How many remediations are open, overdue, breached and completed.
 *
 * **A remediation is one review's set of actions, not one action.** A review raises one
 * action per thing the checker marked down (Remediation.Raise), so a check with five fail
 * items is five rows and one remediation. Counting rows reported the issue total instead -
 * 47 where the business had far fewer - and nothing on the card said that was what it meant.
 *
 * The grouping key is the review instance, which is what remediationForm.settledChecks
 * already groups by: "a Tax-then-AQS case remediates twice and the two are separate
 * remediations - its Tax check raises its own actions and its AQS check raises its own".
 * Grouping by case would merge those two into one and under-report the work.
 *
 * A remediation is completed only when EVERY action on it is completed - "approved as a
 * whole", which is the rule SignoffProgressPlugin enforces server-side before a case may
 * leave Awaiting Sign-off. One unfinished action keeps the whole thing open, and it is
 * overdue or breached if any of its open actions is.
 *
 * An action carrying no review link counts as its own remediation. Rows written before that
 * link existed carry none, and bucketing all of them under one key would report every legacy
 * action on the site as a single remediation.
 */
export function remediationTotals(actions: RemediationCountSource[]): RemediationTotals {
  const groups = new Map<string, { open: boolean; overdue: boolean; breached: boolean }>();

  for (const action of actions) {
    const key = action._al_reviewinstanceid_value ?? `action:${action.al_remediationactionid}`;
    let group = groups.get(key);
    if (!group) {
      group = { open: false, overdue: false, breached: false };
      groups.set(key, group);
    }

    // The formatted name when Dataverse returned one, else the option-set map. Reading the
    // name alone would treat a completed action as open whenever the formatted value is
    // absent, which would silently inflate the open count.
    const status =
      action.al_actionstatusname ??
      Al_remediationactionsal_actionstatus[
        action.al_actionstatus as keyof typeof Al_remediationactionsal_actionstatus
      ];
    if (status === 'Completed') continue;

    group.open = true;
    if (isOverdue(action.al_duedate)) group.overdue = true;
    // The current period, not the whole time in remediation: a rejected sign-off restarts
    // the clock (OD-018), and counting from the original start would report a reworked
    // action as breached before the adviser had had a day on it.
    if (remediationClock(action).breached) group.breached = true;
  }

  const totals: RemediationTotals = { open: 0, overdue: 0, breached: 0, completed: 0 };
  for (const group of groups.values()) {
    if (!group.open) {
      totals.completed += 1;
      continue;
    }
    totals.open += 1;
    if (group.overdue) totals.overdue += 1;
    if (group.breached) totals.breached += 1;
  }

  return totals;
}
