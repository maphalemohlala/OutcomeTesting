import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';

import type { RemediationActionRow } from '../remediation/remediationMapping';

vi.mock('../remediation/useRemediation', () => ({ useRemediation: () => ({ status: 'loading' }) }));

const { CaseRemedialActions } = await import('./CaseRemedialActions');

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
    expect(html).toContain('<td>1</td>');
    expect(html).toContain('<td>2</td>');
    // The record owner is a system account, never the person who owes the action.
    expect(html).not.toContain('Service Account');
  });

  it('says nobody is assigned rather than leaving the owner blank', () => {
    const html = renderToStaticMarkup(<CaseRemedialActions actions={[action({ assignedTo: null })]} />);
    expect(html).toContain('Nobody assigned');
  });
});
