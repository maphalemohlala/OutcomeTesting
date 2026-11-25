import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import type { RemediationActionRow } from '../remediation/remediationMapping';

vi.mock('../remediation/useRemediation', () => ({ useRemediation: () => ({ status: 'loading' }) }));

const { CaseRemedialActions, remedialSummary } = await import('./CaseRemedialActions');

function action(overrides: Partial<RemediationActionRow>): RemediationActionRow {
  return {
    id: 'a1',
    reference: 'Remediation 300000006',
    description: 'Issues found on the check:\n- ID verification: No\n- Client objectives: Fail\n',
    status: 'Open',
    dueOn: '05 Oct 2026',
    completedOn: null,
    triggeredBy: 'AQS check',
    owner: 'Service Account',
    rowVersion: null,
    remedialAction: 'Re-verify ID and file the evidence.',
    actionPerformed: null,
    adviserNote: null,
    evidenceReference: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    assignedTo: 'Adam Strumidlo',
    createdOn: '2026-09-25T10:00:00Z',
    clockStartedOn: null,
    completedOnRaw: null,
    active: true,
    ...overrides,
  };
}

describe('the case page lists the remedial actions', () => {
  it('draws nothing when the case has none', () => {
    expect(renderToStaticMarkup(<CaseRemedialActions actions={[]} />)).toBe('');
  });

  it('draws one numbered row per fail point with the action, adviser, date and status', () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({})]} />);

    expect(html).toContain('<h2 id="panel-remedial">Remedial actions</h2>');
    expect(html).toContain('ID verification: No');
    expect(html).toContain('Client objectives: Fail');
    expect(html).toContain('Re-verify ID and file the evidence.');
    expect(html).toContain('Adam Strumidlo');
    expect(html).toContain('05 Oct 2026');
    expect(html).toContain('AQS check');
    // Numbered straight through, one number per fail point.
    expect(html).toContain('<span class="remedial__number">1</span>');
    expect(html).toContain('<span class="remedial__number">2</span>');
    // The record owner is a system account, never the person who owes the action.
    expect(html).not.toContain('Service Account');
  });

  it('says nobody is assigned rather than leaving the owner blank', () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({ assignedTo: null })]} />);
    expect(html).toContain('Nobody assigned');
  });

  it('leaves out a deactivated action, as the emailed document does', () => {
    const html = renderToStaticMarkup(
      <CaseRemedialActions
        actions={[
          action({}),
          action({ id: 'a2', active: false, description: 'Issues found on the check:\n- Withdrawn point: No\n' }),
        ]}
      />,
    );

    expect(html).toContain('ID verification: No');
    expect(html).not.toContain('Withdrawn point');
  });

  it('draws nothing when every action is deactivated', () => {
    expect(renderToStaticMarkup(<CaseRemedialActions actions={[action({ active: false })]} />)).toBe('');
  });

  it('sums up how many are completed and how many were not performed', () => {
    expect(remedialSummary([action({})])).toBe('0 of 1 completed');
    expect(
      remedialSummary([
        action({ completedOn: '05 Oct 2026', actionPerformed: 'Yes' }),
        action({ id: 'a2', completedOn: '06 Oct 2026', actionPerformed: 'No' }),
        action({ id: 'a3' }),
      ]),
    ).toBe('2 of 3 completed, 1 not performed');
  });
});

describe('the case page shows what the adviser and the supervisor wrote', () => {
  // Reported 2026-10-07: "When an adviser signs off a case, the notes need to be shown on the
  // remedial details and case details as well".
  const decision = {
    id: 's1',
    reference: 'SO',
    decision: 'Rejected',
    notes: 'Client letter unsigned.',
    signedOffOn: '06 Oct 2026',
    signedOffOnRaw: '2026-10-06T10:00:00Z',
    remediationAction: null,
    remediationActionId: 'a1',
    signedOffBy: 'A Supervisor',
  };

  it("draws the adviser's answer and note", () => {
    const html = renderToStaticMarkup(
      <CaseRemedialActions actions={[action({ actionPerformed: 'Yes', adviserNote: 'ID re-verified on 3 Oct.' })]} />,
    );
    expect(html).toContain('Action performed');
    expect(html).toContain('ID re-verified on 3 Oct.');
  });

  it("draws the supervisor's decision and notes", () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({})]} signoffs={[decision]} />);
    expect(html).toContain('Sign-off');
    expect(html).toContain('Rejected, 06 Oct 2026');
    expect(html).toContain('Client letter unsigned.');
  });

  it('says a completed action awaits the supervisor only while the case does', () => {
    const done = action({ completedOn: '05 Oct 2026', completedOnRaw: '2026-10-05' });
    expect(renderToStaticMarkup(<CaseRemedialActions actions={[done]} caseStatus="Awaiting Sign-off" />))
      .toContain('awaiting supervisor');
    expect(renderToStaticMarkup(<CaseRemedialActions actions={[done]} caseStatus="Closed" />))
      .not.toContain('awaiting supervisor');
  });
});
