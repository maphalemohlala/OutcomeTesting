import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import type { CaseSummary } from './caseWorklistMapping';

const cases = [
  {
    id: 'c1',
    caseReference: '991116101',
    route: 'AQS only',
    status: 'Queued',
    createdOn: '2026-11-16T09:30:00Z',
    ageInDays: 9,
    nextAction: '',
  },
  { id: 'c2', caseReference: '991116102', route: 'AQS only', status: 'Queued', createdOn: null, ageInDays: 0, nextAction: '' },
] as unknown as CaseSummary[];

vi.mock('./useCaseWorklist', () => ({
  useCaseWorklist: () => ({ status: 'ready', cases, remediationKnown: false, outcomes: [], actions: [] }),
}));
vi.mock('../../hooks/useUserDirectory', () => ({ useUserDirectory: () => ({ status: 'loading' }) }));

const { CaseWorklistPage } = await import('./CaseWorklistPage');

describe('the case worklist', () => {
  const html = renderToStaticMarkup(
    <MemoryRouter>
      <CaseWorklistPage />
    </MemoryRouter>,
  );

  it('shows the date each case was added', () => {
    expect(html).toContain('<th scope="col">Added on</th>');
    expect(html).toContain('<td>16 Nov 2026</td>');
  });

  it('shows a dash when the date is missing', () => {
    expect(html).toContain('<td>—</td>');
  });
});
