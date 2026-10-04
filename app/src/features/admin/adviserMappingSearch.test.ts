import { describe, expect, it } from 'vitest';
import { filterMappings, toMappingRows, type AdviserMappingRow, type ContactOption } from './adviserMappingRows';

/**
 * Search on the adviser mapping (owner request, 2026-10-02: "search on either adviser or
 * manager"). One box, matched against the adviser's email and name and the manager's name and
 * email, ignoring case and matching part of a word.
 */

const contacts: ContactOption[] = [
  { id: 'c-adam', name: 'Adam Strumidlo', email: 'adam.strumidlo@example.invalid' },
  { id: 'c-angela', name: 'Angela Houghton', email: 'angela.houghton@example.invalid' },
  { id: 'c-zoe', name: 'Zoe Ramwell', email: 'zoe.ramwell@example.invalid' },
];

const rows: AdviserMappingRow[] = toMappingRows(
  [
    { al_advisermappingid: 'm1', al_adviseremail: 'Adam.Strumidlo@example.invalid', _al_tcmanagerid_value: 'c-angela' },
    { al_advisermappingid: 'm2', al_adviseremail: 'aaron.banasik@example.invalid', _al_tcmanagerid_value: 'c-zoe' },
    { al_advisermappingid: 'm3', al_adviseremail: 'no.contact@example.invalid', _al_tcmanagerid_value: null },
  ],
  contacts,
);

const ids = (found: AdviserMappingRow[]) => found.map((r) => r.id);

describe('toMappingRows for search', () => {
  it('names the adviser from the contact with that email, whatever its case', () => {
    expect(rows[0].adviserName).toBe('Adam Strumidlo');
  });

  it('leaves the adviser unnamed when no contact has the email', () => {
    expect(rows[2].adviserName).toBeNull();
  });

  it('carries the manager email from the chosen contact', () => {
    expect(rows[0].managerEmail).toBe('angela.houghton@example.invalid');
    expect(rows[2].managerEmail).toBeNull();
  });
});

describe('filterMappings', () => {
  it('returns every row for an empty or blank search', () => {
    expect(ids(filterMappings(rows, ''))).toEqual(['m1', 'm2', 'm3']);
    expect(ids(filterMappings(rows, '   '))).toEqual(['m1', 'm2', 'm3']);
  });

  it('finds an adviser by part of their name, ignoring case', () => {
    expect(ids(filterMappings(rows, 'strumid'))).toEqual(['m1']);
  });

  it('finds an adviser by email', () => {
    expect(ids(filterMappings(rows, 'aaron.banasik@'))).toEqual(['m2']);
  });

  it('finds every adviser of a manager by the manager name', () => {
    expect(ids(filterMappings(rows, 'HOUGHTON'))).toEqual(['m1']);
  });

  it('finds a manager by email', () => {
    expect(ids(filterMappings(rows, 'zoe.ramwell'))).toEqual(['m2']);
  });

  it('ignores spaces around the search', () => {
    expect(ids(filterMappings(rows, '  zoe '))).toEqual(['m2']);
  });

  it('returns nothing when nothing matches', () => {
    expect(filterMappings(rows, 'nobody')).toEqual([]);
  });
});
