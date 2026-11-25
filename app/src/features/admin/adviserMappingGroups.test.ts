import { describe, expect, it } from 'vitest';
import { filterAdvisers, groupByAdviser, managerLoads, type AdviserMappingRow } from './adviserMappingRows';

/** An adviser may have several T&C Managers, all equal (owner, 2026-11-24): one row each. */

const row = (id: string, adviserEmail: string, managerId: string, managerName: string): AdviserMappingRow => ({
  id,
  adviserEmail,
  adviserName: adviserEmail.startsWith('ann') ? 'Ann Adviser' : null,
  managerId,
  managerName,
  managerEmail: `${managerId}@example.invalid`,
});

const rows = [
  row('1', 'ann@example.invalid', 'pat', 'Pat Manager'),
  row('2', 'bob@example.invalid', 'pat', 'Pat Manager'),
  row('3', 'ANN@example.invalid', 'sam', 'Sam Manager'),
];

describe('groupByAdviser', () => {
  it('shows each adviser once with every manager, matching email whatever its case', () => {
    const groups = groupByAdviser(rows);

    expect(groups.map((g) => g.adviserEmail)).toEqual(['ann@example.invalid', 'bob@example.invalid']);
    expect(groups[0].managers.map((m) => m.managerName)).toEqual(['Pat Manager', 'Sam Manager']);
  });
});

describe('filterAdvisers', () => {
  it('keeps all of an adviser’s managers when the search matches only one of them', () => {
    const shown = filterAdvisers(rows, 'sam');

    expect(shown).toHaveLength(1);
    expect(shown[0].managers.map((m) => m.managerName)).toEqual(['Pat Manager', 'Sam Manager']);
  });

  it('keeps every adviser for a blank search', () => {
    expect(filterAdvisers(rows, '  ')).toHaveLength(2);
  });
});

describe('managerLoads', () => {
  it('counts an adviser under each of their managers', () => {
    const loads = managerLoads(rows);

    expect(loads.map((l) => [l.name, l.advisers.length])).toEqual([
      ['Pat Manager', 2],
      ['Sam Manager', 1],
    ]);
  });
});
