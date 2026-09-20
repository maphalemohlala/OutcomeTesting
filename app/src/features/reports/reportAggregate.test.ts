import { describe, expect, it } from 'vitest';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';
import type { Al_remediationactions } from '../../generated/models/Al_remediationactionsModel';
import { aggregate } from './reportAggregate';

/**
 * Found in DEV on 2026-09-20. The Management reporting page contradicted itself, on one
 * screen, from one set of data:
 *
 *   1 RECORDED OUTCOMES   1 FINALISED OUTCOMES   1 OPEN REMEDIATION
 *   Outcome volumes:   Pass 0  Pass with issues 0  Insufficient evidence 0  Potential harm 0
 *   Remediation ageing: 1-5 days 0  6-10 days 0  11-20 days 0  Over 20 days 0
 *
 * Case 900000001 was graded Pass with issues and carried one open remediation action.
 * Both breakdowns reported nothing while their own headline tiles reported one.
 *
 * Two separate causes, and both under-report. For a compliance product this is the worst
 * direction to be wrong in: a grade that is never counted is a grade nobody has to explain.
 */

function outcome(fields: Partial<Al_outcomes>): Al_outcomes {
  return { al_initialoutcome: 120910701, ...fields } as Al_outcomes;
}

function action(fields: Partial<Al_remediationactions>): Al_remediationactions {
  return { al_actionstatusname: 'Open', ...fields } as Al_remediationactions;
}

describe('outcome volumes count a case that has no final outcome yet', () => {
  /**
   * Dataverse holds no al_finaloutcome on this row, but the SDK hands the property over as
   * null rather than leaving it off. The generated type says `al_finaloutcome?:` — optional,
   * never null — so the compiler agrees with a test that only ever omits it, and the bug
   * lives entirely in the gap between that type and what arrives at runtime. Hence the cast.
   */
  it('treats a null final outcome as not yet finalised', () => {
    const data = aggregate([outcome({ al_finaloutcome: null as never })], [], []);

    expect(data.finalisedCount).toBe(0);
  });

  it('falls back to the initial outcome when the final one is null', () => {
    const data = aggregate([outcome({ al_finaloutcome: null as never })], [], []);
    const passWithIssues = data.outcomeVolumes.find((v) => v.outcome === 'Pass with issues');

    expect(passWithIssues?.count).toBe(1);
  });

  it('still prefers a final outcome that is actually set', () => {
    // BR-007: a regrade overrides the initial grade, and must keep doing so.
    const data = aggregate([outcome({ al_initialoutcome: 120910701, al_finaloutcome: 120910710 })], [], []);

    expect(data.finalisedCount).toBe(1);
    expect(data.outcomeVolumes.find((v) => v.outcome === 'Pass')?.count).toBe(1);
    expect(data.outcomeVolumes.find((v) => v.outcome === 'Pass with issues')?.count).toBe(0);
  });

  it('counts every graded case somewhere', () => {
    // The invariant the page broke: the breakdown and the headline are the same cases.
    const data = aggregate(
      [outcome({ al_finaloutcome: null as never }), outcome({ al_finaloutcome: 120910713 })],
      [],
      [],
    );
    const counted = data.outcomeVolumes.reduce((n, v) => n + v.count, 0);

    expect(counted).toBe(data.outcomeTotal);
  });
});

describe('remediation ageing counts an action raised today', () => {
  it('puts a nought-working-day action in the first band', () => {
    // The first band started at one working day, so an action raised this morning was
    // counted as open and then fell into no band at all.
    const data = aggregate([], [action({ al_duedate: '2099-01-01' })], []);
    const banded = data.remediationAgeing.reduce((n, b) => n + b.count, 0);

    expect(data.openRemediation).toBe(1);
    expect(banded).toBe(1);
  });

  it('bands every open action, so the bands equal the open count', () => {
    const data = aggregate(
      [],
      [action({ al_duedate: '2099-01-01' }), action({ al_duedate: '2099-01-02' })],
      [],
    );

    expect(data.remediationAgeing.reduce((n, b) => n + b.count, 0)).toBe(data.openRemediation);
  });

  it('leaves a completed action out of both', () => {
    const data = aggregate([], [action({ al_actionstatusname: 'Completed' })], []);

    expect(data.openRemediation).toBe(0);
    expect(data.remediationAgeing.reduce((n, b) => n + b.count, 0)).toBe(0);
  });
});
