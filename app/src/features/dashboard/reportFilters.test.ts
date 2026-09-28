import { describe, expect, it } from 'vitest';
import type { CaseSummary } from '../cases/caseWorklistMapping';
import {
  EMPTY_REPORT_FILTERS,
  casesInScope,
  narrowToScope,
  worklistLink,
  type ReportFilters,
} from './reportFilters';

function row(id: string, overrides: Partial<CaseSummary> = {}): CaseSummary {
  return {
    id,
    route: 'AQS only',
    adviser: null,
    taxChecker: null,
    aqsChecker: null,
    createdOn: '2026-09-10T09:00:00Z',
    ...overrides,
  } as CaseSummary;
}

function scope(cases: CaseSummary[], filters: Partial<ReportFilters>) {
  return casesInScope(cases, { ...EMPTY_REPORT_FILTERS, ...filters });
}

describe('casesInScope', () => {
  it('is unscoped when no filter is set, so rows with no case are still counted', () => {
    // The unfiltered figures must not move because filters were added: an outcome or action
    // that names no case counted before and still does.
    expect(scope([row('c1')], {})).toBeNull();
  });

  it('keeps the cases imported inside the date range, inclusive at both ends', () => {
    const cases = [
      row('before', { createdOn: '2026-09-01T23:00:00Z' }),
      row('first', { createdOn: '2026-09-02T08:00:00Z' }),
      row('last', { createdOn: '2026-09-30T18:00:00Z' }),
      row('after', { createdOn: '2026-10-01T08:00:00Z' }),
    ];

    expect([...(scope(cases, { from: '2026-09-02', to: '2026-09-30' }) ?? [])]).toEqual([
      'first',
      'last',
    ]);
  });

  it('keeps the unrouted cases when the route filter says none', () => {
    const cases = [row('routed'), row('unrouted', { route: null })];

    expect([...(scope(cases, { route: 'none' }) ?? [])]).toEqual(['unrouted']);
  });

  it('matches a checker in either discipline', () => {
    const cases = [
      row('tax', { taxChecker: 'C. Checker' }),
      row('aqs', { aqsChecker: 'C. Checker' }),
      row('neither', { aqsChecker: 'D. Checker' }),
    ];

    expect([...(scope(cases, { checker: 'C. Checker' }) ?? [])]).toEqual(['tax', 'aqs']);
  });
});

describe('narrowToScope', () => {
  const rows = [{ caseId: 'c1' }, { caseId: 'c2' }, { caseId: undefined }];

  it('keeps every row when unscoped', () => {
    expect(narrowToScope(rows, null, (r) => r.caseId)).toHaveLength(3);
  });

  it('keeps only the rows on a case in scope, and drops those naming no case', () => {
    // A row with no case cannot be shown to belong to the adviser or dates chosen, so a
    // filtered figure leaves it out rather than guessing.
    expect(narrowToScope(rows, new Set(['c2']), (r) => r.caseId)).toEqual([{ caseId: 'c2' }]);
  });
});

describe('worklistLink', () => {
  it('carries the dashboard filters into the worklist it opens', () => {
    // Clicking a filtered figure has to open the list that figure counted.
    const filters = { ...EMPTY_REPORT_FILTERS, from: '2026-09-01', checker: 'C. Checker' };

    expect(worklistLink('/cases?status=Closed', filters)).toBe(
      '/cases?status=Closed&from=2026-09-01&checker=C.+Checker',
    );
  });

  it('leaves the link alone when nothing is filtered', () => {
    expect(worklistLink('/cases', EMPTY_REPORT_FILTERS)).toBe('/cases');
  });
});
