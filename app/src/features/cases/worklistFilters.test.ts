import { describe, expect, it } from 'vitest';
import type { CaseSummary } from './caseWorklistMapping';
import { applyFilters, EMPTY_FILTERS, type Filters } from './worklistFilters';

function row(id: string, overrides: Partial<CaseSummary> = {}): CaseSummary {
  return {
    id,
    caseReference: id,
    route: 'AQS only',
    status: 'Awaiting Remediation',
    adviser: null,
    taxChecker: null,
    aqsChecker: null,
    createdOn: '2026-09-10T09:00:00Z',
    ...overrides,
  } as CaseSummary;
}

function filtered(cases: CaseSummary[], filters: Partial<Filters>): string[] {
  return applyFilters(cases, { ...EMPTY_FILTERS, ...filters }).map((item) => item.id);
}

describe('the worklist remediation filter', () => {
  const cases = [
    row('open', { remediation: { state: 'open', open: 8, total: 8 } }),
    row('complete', { remediation: { state: 'complete', open: 0, total: 3 } }),
    row('none', { remediation: null }),
  ];

  it('finds the cases whose remediation is complete', () => {
    // The request of 2026-09-28: a clear way to list the completed remediation cases. A
    // completed one sits at Awaiting Sign-off or Closed, where the status filter cannot
    // tell it apart from a case that never needed remediation.
    expect(filtered(cases, { remediation: 'complete' })).toEqual(['complete']);
  });

  it('finds the cases with remediation still open', () => {
    expect(filtered(cases, { remediation: 'open' })).toEqual(['open']);
  });

  it('finds the cases that never raised any', () => {
    expect(filtered(cases, { remediation: 'none' })).toEqual(['none']);
  });
});

describe('the adviser and checker filters a dashboard hands over', () => {
  const cases = [
    row('tax', { adviser: 'A. Adviser', taxChecker: 'C. Checker' }),
    row('aqs', { adviser: 'B. Adviser', aqsChecker: 'C. Checker' }),
    row('other', { adviser: 'A. Adviser', aqsChecker: 'D. Checker' }),
  ];

  it('matches the checker in either discipline', () => {
    expect(filtered(cases, { checker: 'C. Checker' })).toEqual(['tax', 'aqs']);
  });

  it('matches the adviser only as adviser', () => {
    expect(filtered(cases, { adviser: 'A. Adviser' })).toEqual(['tax', 'other']);
  });
});
