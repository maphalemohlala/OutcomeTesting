import { describe, expect, it } from 'vitest';
import { holdingSummary, matrixColumns, matrixRows } from './securityMatrix';

const roles = [
  { code: 'Administrators', active: true },
  { code: 'AL Portal - AQS Reviewer', active: true },
  { code: 'Old Role', active: false },
];
const mappings = [
  { id: '1', email: 'Clare@x.com', role: 'AL Portal - AQS Reviewer', active: false },
  { id: '2', email: 'clare@x.com', role: 'al portal - aqs reviewer', active: true },
  { id: '3', email: 'clare@x.com', role: 'Old Role', active: false },
  { id: '4', email: 'ann@x.com', role: 'Administrators', active: true },
];

describe('security matrix', () => {
  const columns = matrixColumns(roles, mappings);

  it('shows live roles, then a retired role still named by an assignment, Administrators last', () => {
    expect(columns.map((c) => c.label)).toEqual(['AQS Reviewer', 'Old Role', 'Administrators']);
    expect(columns.map((c) => c.retired)).toEqual([false, true, false]);
    expect(columns.at(-1)?.privileged).toBe(true);
  });

  it('merges one person whatever the email case, and an active assignment wins', () => {
    const rows = matrixRows(mappings, columns);
    expect(rows).toHaveLength(2);
    const clare = rows[0];
    expect(clare.cells.map((c) => c.state)).toEqual(['held', 'withdrawn', 'none']);
    expect(clare.cells[0].mapping?.id).toBe('2');
    expect(holdingSummary(clare)).toBe('1 active, 1 withdrawn');
    expect(holdingSummary(rows[1])).toBe('1 active');
  });
});
