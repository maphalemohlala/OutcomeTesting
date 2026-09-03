import { describe, expect, it } from 'vitest';
import { buildDirectory, casesForPerson, isPersonRole } from './peopleDirectory';
import type { CaseSummary } from '../cases/caseWorklistMapping';

function caseRow(overrides: Partial<CaseSummary>): CaseSummary {
  return {
    id: 'case-1',
    caseReference: 'IO-1',
    route: null,
    status: 'Assigned',
    owner: null,
    priority: null,
    createdOn: null,
    ageInDays: 0,
    latestOutcome: null,
    initialOutcome: null,
    finalOutcome: null,
    finalisedOn: null,
    nextAction: '',
    client: null,
    adviser: null,
    adviserCode: null,
    paraplanner: null,
    paraplannerCode: null,
    checker: null,
    caseType: null,
    productSolutionType: null,
    products: null,
    adviceDate: null,
    checkDate: null,
    preOrPostCheck: null,
    dueDate: null,
    ...overrides,
  };
}

describe('buildDirectory', () => {
  it('counts one person once per position they hold on a case', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane Adviser', checker: 'Jane Adviser' }),
    ]);

    expect(directory.map((p) => p.role).sort()).toEqual(['Adviser', 'Checker']);
    expect(directory.every((p) => p.totalCases === 1)).toBe(true);
  });

  it('splits open from closed and records the oldest open case', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane', status: 'Closed', ageInDays: 90 }),
      caseRow({ id: 'b', adviser: 'Jane', status: 'Assigned', ageInDays: 12 }),
      caseRow({ id: 'c', adviser: 'Jane', status: 'No Check Required', ageInDays: 40 }),
    ]);

    const jane = directory[0];
    expect(jane.totalCases).toBe(3);
    expect(jane.openCases).toBe(1);
    expect(jane.closedCases).toBe(2);
    expect(jane.oldestOpenDays).toBe(12);
  });

  it('tallies the BR-005 grades and what remains ungraded', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane', latestOutcome: 'Pass' }),
      caseRow({ id: 'b', adviser: 'Jane', latestOutcome: 'Potential harm' }),
      caseRow({ id: 'c', adviser: 'Jane' }),
    ]);

    const jane = directory[0];
    expect(jane.outcomes.Pass).toBe(1);
    expect(jane.outcomes['Potential harm']).toBe(1);
    expect(jane.outcomes['Pass with issues']).toBe(0);
    expect(jane.notGraded).toBe(1);
  });

  it('keeps the adviser code from whichever case carries it', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Jane' }),
      caseRow({ id: 'b', adviser: 'Jane', adviserCode: 'ADV-01' }),
    ]);

    expect(directory[0].code).toBe('ADV-01');
  });

  it('ignores blank and whitespace-only names rather than creating a nameless person', () => {
    expect(buildDirectory([caseRow({ adviser: '   ', paraplanner: '' })])).toEqual([]);
  });

  it('orders the busiest person first', () => {
    const directory = buildDirectory([
      caseRow({ id: 'a', adviser: 'Quiet' }),
      caseRow({ id: 'b', adviser: 'Busy' }),
      caseRow({ id: 'c', adviser: 'Busy' }),
    ]);

    expect(directory[0].name).toBe('Busy');
  });
});

describe('casesForPerson', () => {
  const cases = [
    caseRow({ id: 'a', adviser: 'Jane Adviser' }),
    caseRow({ id: 'b', checker: 'Jane Adviser' }),
    caseRow({ id: 'c', adviser: 'Someone Else' }),
  ];

  it('returns only the cases where the person holds that position', () => {
    expect(casesForPerson(cases, 'Adviser', 'Jane Adviser').map((c) => c.id)).toEqual(['a']);
    expect(casesForPerson(cases, 'Checker', 'Jane Adviser').map((c) => c.id)).toEqual(['b']);
  });

  it('matches case-insensitively so a link survives a differently cased name', () => {
    expect(casesForPerson(cases, 'Adviser', 'jane adviser')).toHaveLength(1);
  });
});

describe('isPersonRole', () => {
  it('rejects an unknown position from the URL', () => {
    expect(isPersonRole('Adviser')).toBe(true);
    expect(isPersonRole('Administrator')).toBe(false);
    expect(isPersonRole(undefined)).toBe(false);
  });
});
