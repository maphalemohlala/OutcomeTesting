import { describe, expect, it } from 'vitest';

import { remediationForm, remediationFormLayout, signoffCell } from './remediationForm';
import type { OutcomeRow, RemediationActionRow, SignoffRow } from './remediationMapping';

function action(over: Partial<RemediationActionRow> = {}): RemediationActionRow {
  return {
    id: 'a',
    reference: 'REM-1',
    description: '',
    status: 'Open',
    dueOn: '22 Sep 2026',
    completedOn: null,
    completedOnRaw: null,
    triggeredBy: null,
    owner: null,
    rowVersion: null,
    remedialAction: null,
    evidenceReference: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    assignedTo: null,
    createdOn: '2026-09-08T09:00:00Z',
    clockStartedOn: null,
    ...over,
  };
}

function signoff(over: Partial<SignoffRow> = {}): SignoffRow {
  return {
    id: 's',
    reference: 'SO-1',
    decision: 'Approved',
    notes: null,
    signedOffOn: '2026-09-09',
    remediationAction: null,
    remediationActionId: 'a',
    signedOffBy: 'Seed Supervisor',
    ...over,
  };
}

function outcome(over: Partial<OutcomeRow> = {}): OutcomeRow {
  return {
    id: 'o',
    reference: 'OUT-1',
    initialOutcome: 'Insufficient evidence',
    finalOutcome: null,
    regradeReason: null,
    finalisedOn: null,
    regradedOn: null,
    reviewInstance: null,
    ...over,
  };
}

describe('signoffCell', () => {
  it('gives the supervisor decision once there is one', () => {
    expect(signoffCell(action(), [signoff()])).toBe('Approved, 2026-09-09');
  });

  it('says the action is waiting on the supervisor when only the adviser has finished', () => {
    expect(signoffCell(action({ completedOn: '20 Sep 2026', completedOnRaw: '2026-09-20' }), [])).toBe(
      'Adviser completed 20 Sep 2026; awaiting supervisor',
    );
  });

  it('has nothing to say for an action nobody has acted on', () => {
    expect(signoffCell(action(), [])).toBe('—');
  });

  it('takes the latest decision, not the first recorded', () => {
    const older = signoff({ id: 's1', decision: 'Rejected', signedOffOn: '2026-09-08' });
    const newer = signoff({ id: 's2', decision: 'Approved', signedOffOn: '2026-09-10' });
    expect(signoffCell(action(), [older, newer])).toBe('Approved, 2026-09-10');
  });

  it('ignores a sign-off against a different action', () => {
    // Every action a review raises shares one al_name, so this has to match on the id.
    expect(signoffCell(action({ id: 'a' }), [signoff({ remediationActionId: 'b' })])).toBe('—');
  });
});

describe('remediationForm', () => {
  it('takes the three answers from the first action carrying any', () => {
    const answered = action({
      id: 'b',
      clientContactRequired: 'Yes',
      recheckRequired: 'No',
      changesAdvice: 'No',
    });
    const block = remediationForm([action({ id: 'a' }), answered], [], []);

    expect(block.clientContactRequired).toBe('Yes');
    expect(block.recheckRequired).toBe('No');
    expect(block.changesAdvice).toBe('No');
  });

  it('reads Yes only when every action has been approved', () => {
    const one = action({ id: 'a' });
    const two = action({ id: 'b' });
    const block = remediationForm(
      [one, two],
      [],
      [signoff({ id: 's1', remediationActionId: 'a' }), signoff({ id: 's2', remediationActionId: 'b' })],
    );

    expect(block.allApproved).toBe('Yes');
  });

  it('leaves it unanswered while an action has no decision, rather than passing it', () => {
    const block = remediationForm(
      [action({ id: 'a' }), action({ id: 'b' })],
      [],
      [signoff({ remediationActionId: 'a' })],
    );

    expect(block.allApproved).toBeNull();
  });

  it('reads No once any action has been rejected', () => {
    const block = remediationForm(
      [action({ id: 'a' })],
      [],
      [signoff({ decision: 'Rejected' })],
    );

    expect(block.allApproved).toBe('No');
  });

  it('has nothing to approve when no action has been raised', () => {
    expect(remediationForm([], [], []).allApproved).toBeNull();
  });

  it('reports the regrade, falling back to the finalised date', () => {
    const regraded = remediationForm(
      [],
      [outcome({ finalOutcome: 'Pass', regradedOn: '18 Sep 2026', finalisedOn: '10 Sep 2026' })],
      [],
    );
    expect(regraded.regradedOutcome).toBe('Pass');
    expect(regraded.regradedOn).toBe('18 Sep 2026');

    const finalised = remediationForm([], [outcome({ finalisedOn: '10 Sep 2026' })], []);
    expect(finalised.regradedOn).toBe('10 Sep 2026');
  });

  it('names the supervisor, their decision and the date', () => {
    const block = remediationForm([action()], [], [signoff()]);
    expect(block.supervisorSignoff).toBe('Approved, Seed Supervisor, 2026-09-09');
  });

  it('takes the adviser sign-off from the most recently completed action', () => {
    const early = action({
      id: 'a',
      assignedTo: 'Seed Adviser 01',
      completedOn: '15 Sep 2026',
      completedOnRaw: '2026-09-15',
    });
    const late = action({
      id: 'b',
      assignedTo: 'Seed Adviser 02',
      completedOn: '20 Sep 2026',
      completedOnRaw: '2026-09-20',
    });

    const block = remediationForm([early, late], [], []);

    expect(block.adviserSignoff).toBe('Seed Adviser 02, 20 Sep 2026');
  });

  it('has no adviser sign-off while nothing is completed', () => {
    expect(remediationForm([action()], [], []).adviserSignoff).toBeNull();
  });
});

describe('the paper form layout (project owner, 2026-09-10)', () => {
  const block = remediationForm([], [], []);

  it('lays the four answers out two to a row, as the document draws them', () => {
    const layout = remediationFormLayout(block);

    expect(layout.answers.map((row) => row.map((cell) => cell.label))).toEqual([
      ['Client contact required?', 'Recheck required?'],
      ['Do the remedial actions change the advice?', 'All remedial actions checked and approved?'],
    ]);
  });

  it('keeps the paper note that says who signs the regrade off', () => {
    const layout = remediationFormLayout(block);

    expect(layout.regrade.label).toBe('Regraded outcome');
    expect(layout.regrade.note).toContain('supervisor');
  });

  it('draws the two sign-offs as signatory and date', () => {
    const layout = remediationFormLayout(block);

    expect(layout.signoffs.map((s) => s.label)).toEqual([
      'Supervisor sign-off',
      'Adviser sign-off',
    ]);
    for (const signoff of layout.signoffs) {
      expect(signoff).toHaveProperty('by');
      expect(signoff).toHaveProperty('on');
    }
  });

  it('carries the sign-off dates as their own values, not folded into the name', () => {
    const signed = remediationForm(
      [action({ id: 'a', assignedTo: 'Adviser One', completedOn: '20 Sep 2026', completedOnRaw: '2026-09-20' })],
      [],
      [],
    );

    expect(remediationFormLayout(signed).signoffs[1].on).toBe('20 Sep 2026');
  });
});

