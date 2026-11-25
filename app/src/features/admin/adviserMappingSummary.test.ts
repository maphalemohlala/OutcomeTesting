import { describe, expect, it } from 'vitest';
import { isSelfMapped, managerLoads, type AdviserMappingRow } from './adviserMappingRows';

function row(over: Partial<AdviserMappingRow>): AdviserMappingRow {
  return {
    id: 'r',
    adviserEmail: 'a@example.invalid',
    adviserName: 'Ann Adviser',
    managerId: 'm1',
    managerName: 'Max Manager',
    managerEmail: 'm@example.invalid',
    ...over,
  };
}

describe('isSelfMapped', () => {
  it('matches the adviser and manager by email, ignoring case and spaces', () => {
    expect(isSelfMapped(row({ managerEmail: ' A@Example.invalid ' }))).toBe(true);
  });

  it('never matches by name alone', () => {
    expect(isSelfMapped(row({ managerName: 'Ann Adviser' }))).toBe(false);
  });

  it('is false when the manager has no email', () => {
    expect(isSelfMapped(row({ managerEmail: null }))).toBe(false);
  });
});

describe('managerLoads', () => {
  it('groups advisers under their manager, most advisers first, and skips rows with none', () => {
    const loads = managerLoads([
      row({ id: '1', managerId: 'M2', managerName: 'Zed', adviserName: null, adviserEmail: 'x@example.invalid' }),
      row({ id: '2' }),
      row({ id: '3', managerId: 'm1', adviserName: 'Bob' }),
      row({ id: '4', managerId: null }),
    ]);
    expect(loads.map((l) => [l.name, l.advisers])).toEqual([
      ['Max Manager', ['Ann Adviser', 'Bob']],
      ['Zed', ['x@example.invalid']],
    ]);
  });
});
