import { describe, expect, it } from 'vitest';
import { remediationTotals, type RemediationCountSource } from './remediationTotals';

const COMPLETED = 120910602;
const OPEN = 120910600;

/** A long-past due date and clock start, so "overdue" and "breached" are deterministic. */
const LONG_AGO = '2020-01-06T09:00:00Z';

/**
 * Raised just now, so an action that says nothing about its own clock is never breached.
 *
 * This was a fixed 2026-09-01. remediationClock falls back to createdon when there is no
 * al_clockstartedon, and breaches past ten working days, so the suite passed while that
 * date was recent and began failing on 2026-09-19, once it was fourteen working days back
 * - a calendar failure reported as a counting bug. The tests that DO exercise the clock
 * pass LONG_AGO explicitly and are unaffected.
 */
const JUST_RAISED = new Date().toISOString();

function action(
  id: string,
  review: string | undefined,
  status: number,
  extra: Partial<RemediationCountSource> = {},
): RemediationCountSource {
  return {
    al_remediationactionid: id,
    al_actionstatus: status,
    al_actionstatusname: status === COMPLETED ? 'Completed' : 'Open',
    _al_reviewinstanceid_value: review,
    createdon: JUST_RAISED,
    ...extra,
  };
}

describe('remediationTotals', () => {
  it('counts one remediation per review, not one per fail item', () => {
    // The reported bug: a check with five marked-down items is five action rows and ONE
    // remediation. Counting rows is what produced 47.
    const actions = [
      action('a1', 'rev-1', COMPLETED),
      action('a2', 'rev-1', COMPLETED),
      action('a3', 'rev-1', COMPLETED),
      action('a4', 'rev-1', COMPLETED),
      action('a5', 'rev-1', COMPLETED),
    ];

    expect(remediationTotals(actions)).toEqual({
      open: 0,
      overdue: 0,
      breached: 0,
      completed: 1,
    });
  });

  it('keeps a Tax-then-AQS case as two remediations, because each check raises its own', () => {
    const actions = [
      action('t1', 'rev-tax', COMPLETED),
      action('t2', 'rev-tax', COMPLETED),
      action('a1', 'rev-aqs', COMPLETED),
    ];

    expect(remediationTotals(actions).completed).toBe(2);
  });

  it('is completed only when every action on the review is, not when some are', () => {
    // "Approved as a whole" - the rule SignoffProgressPlugin enforces server-side.
    const actions = [
      action('a1', 'rev-1', COMPLETED),
      action('a2', 'rev-1', COMPLETED),
      action('a3', 'rev-1', OPEN),
    ];

    const totals = remediationTotals(actions);
    expect(totals.completed).toBe(0);
    expect(totals.open).toBe(1);
  });

  it('counts an action with no review link as its own remediation', () => {
    // Rows written before the review link existed carry none. Bucketing them together
    // would report every legacy action on the site as one remediation.
    const actions = [
      action('a1', undefined, OPEN),
      action('a2', undefined, OPEN),
      action('a3', undefined, COMPLETED),
    ];

    expect(remediationTotals(actions)).toEqual({
      open: 2,
      overdue: 0,
      breached: 0,
      completed: 1,
    });
  });

  it('marks a remediation overdue or breached when any one of its open actions is', () => {
    const actions = [
      action('a1', 'rev-1', OPEN),
      action('a2', 'rev-1', OPEN, { al_duedate: LONG_AGO, al_clockstartedon: LONG_AGO }),
    ];

    const totals = remediationTotals(actions);
    expect(totals.open).toBe(1);
    expect(totals.overdue).toBe(1);
    expect(totals.breached).toBe(1);
  });

  it('does not let a completed action make its remediation overdue', () => {
    // A settled action keeps its old due date; only what is still open can be late.
    const actions = [action('a1', 'rev-1', COMPLETED, { al_duedate: LONG_AGO })];

    expect(remediationTotals(actions)).toEqual({
      open: 0,
      overdue: 0,
      breached: 0,
      completed: 1,
    });
  });

  it('falls back to the option set when Dataverse returned no formatted status', () => {
    // Reading al_actionstatusname alone would treat this completed action as open.
    const actions: RemediationCountSource[] = [
      {
        al_remediationactionid: 'a1',
        al_actionstatus: COMPLETED,
        _al_reviewinstanceid_value: 'rev-1',
        createdon: JUST_RAISED,
      },
    ];

    expect(remediationTotals(actions).completed).toBe(1);
    expect(remediationTotals(actions).open).toBe(0);
  });

  it('counts nothing for no actions', () => {
    expect(remediationTotals([])).toEqual({ open: 0, overdue: 0, breached: 0, completed: 0 });
  });
});
