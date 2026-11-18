import { describe, expect, it } from 'vitest';

import { remediationForm, signoffCell } from './remediationForm';
import type { RemediationActionRow, SignoffRow } from './remediationMapping';

function action(over: Partial<RemediationActionRow> = {}): RemediationActionRow {
  return {
    id: 'a',
    reference: 'REM-1',
    description: '',
    status: 'Open',
    dueOn: '22 Sep 2026',
    completedOn: null,
    completedOnRaw: null,
    active: true,
    triggeredBy: null,
    owner: null,
    rowVersion: null,
    remedialAction: null,
    actionPerformed: null,
    adviserNote: null,
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
    signedOffOn: '09 Sep 2026',
    signedOffOnRaw: '2026-09-09T10:00:00Z',
    remediationAction: null,
    remediationActionId: 'a',
    signedOffBy: 'Seed Supervisor',
    ...over,
  };
}

describe('an action rejected, then completed again', () => {
  const redone = action({ completedOn: '20 Sep 2026', completedOnRaw: '2026-09-20T09:00:00Z' });
  const rejected = [signoff({ decision: 'Rejected', signedOffOnRaw: '2026-09-10T10:00:00Z' })];

  it('reads as awaiting a new decision in the Sign-off column', () => {
    expect(signoffCell(redone, rejected, 'Awaiting Sign-off')).toContain('awaiting supervisor');
  });

  it('leaves "all approved" unanswered rather than No, agreeing with the column', () => {
    expect(remediationForm([redone], [], rejected).allApproved).toBeNull();
  });

  it('still reads No for a rejection nobody has acted on', () => {
    const untouched = action({ completedOn: '08 Sep 2026', completedOnRaw: '2026-09-08T09:00:00Z' });
    expect(remediationForm([untouched], [], rejected).allApproved).toBe('No');
  });
});
