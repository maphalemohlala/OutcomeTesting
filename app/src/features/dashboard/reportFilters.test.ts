import { describe, expect, it } from 'vitest';
import type { CaseSummary } from '../cases/caseWorklistMapping';
import {
  EMPTY_REPORT_FILTERS,
  adviserOptions,
  casesInScope,
  checkerOptions,
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

  // Item 1, 2026-10-04 review: two checkers sharing a name must stay apart once the filter
  // value is an identity rather than a bare name.
  it('keeps two checkers of one name apart when matched by identity', () => {
    const cases = [
      row('tax', { taxChecker: 'Carol Checker', taxCheckerId: 'c1' }),
      row('aqs', { aqsChecker: 'Carol Checker', aqsCheckerId: 'c2' }),
    ];

    expect([...(scope(cases, { checker: 'contact:c1' }) ?? [])]).toEqual(['tax']);
  });

  // Item 2, 2026-10-04 review: the adviser filter's own legacy value, a bare name from
  // before advisers were keyed by email.
  it('falls back to a name match for a legacy, non-email adviser value', () => {
    const cases = [
      row('1', { adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' }),
      row('2', { adviser: 'Someone Else', adviserEmail: 'someone@example.com' }),
    ];

    expect([...(scope(cases, { adviser: 'Adam Smith' }) ?? [])]).toEqual(['1']);
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

describe('adviserOptions', () => {
  it('offers advisers by email, labelled with name and email', () => {
    const cases = [
      row('1', { adviser: 'Adam Smith', adviserEmail: 'adam.smith@example.com' }),
      row('2', { adviser: 'Adam Smith', adviserEmail: 'adam.smith2@example.com' }),
    ];
    expect(adviserOptions(cases)).toEqual([
      { value: 'adam.smith@example.com', label: 'Adam Smith (adam.smith@example.com)' },
      { value: 'adam.smith2@example.com', label: 'Adam Smith (adam.smith2@example.com)' },
    ]);
  });
});

describe('checkerOptions', () => {
  // Item 1, 2026-10-04 review: options are keyed on identity, so two checkers who share a
  // name offer two distinct choices rather than one the filter cannot tell apart.
  it('offers two checkers of one name as distinct options, keyed by contact id', () => {
    const cases = [
      row('tax', { taxChecker: 'Carol Checker', taxCheckerId: 'c1' }),
      row('aqs', { aqsChecker: 'Carol Checker', aqsCheckerId: 'c2' }),
    ];
    expect(checkerOptions(cases)).toEqual([
      { value: 'contact:c1', label: 'Carol Checker' },
      { value: 'contact:c2', label: 'Carol Checker' },
    ]);
  });

  it('de-duplicates a checker who appears in both disciplines', () => {
    const cases = [row('1', { taxChecker: 'Carol Checker', taxCheckerId: 'c1', aqsChecker: 'Carol Checker', aqsCheckerId: 'c1' })];
    expect(checkerOptions(cases)).toEqual([{ value: 'contact:c1', label: 'Carol Checker' }]);
  });
});
