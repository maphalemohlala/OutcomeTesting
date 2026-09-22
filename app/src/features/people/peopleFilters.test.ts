import { describe, expect, it } from 'vitest';
import { ROLE_FILTERS, matchesRole } from './peopleFilters';

describe('People role filter', () => {
  it('offers the six roles people actually hold', () => {
    // The three checker roles stay distinct (D6): they are what al_Role is seeded with and
    // what the Tax/AQS review routing already distinguishes.
    expect(ROLE_FILTERS).toEqual([
      'Adviser',
      'Paraplanner',
      'T&C Manager',
      'Tax Checker',
      'AQS Checker',
      'Senior Checker',
    ]);
  });

  it('matches anyone holding the role, not only those holding it alone', () => {
    // A person can hold several (D8) - a T&C Manager who also advises is one person, not a
    // reason to show them twice or to hide them from either filter.
    expect(matchesRole(['T&C Manager', 'Adviser'], 'Adviser')).toBe(true);
    expect(matchesRole(['T&C Manager', 'Adviser'], 'T&C Manager')).toBe(true);
  });

  it('lets everyone through when no role is chosen', () => {
    expect(matchesRole([], 'all')).toBe(true);
    expect(matchesRole(['Adviser'], 'all')).toBe(true);
  });

  it('excludes someone who holds no roles when a role is chosen', () => {
    expect(matchesRole([], 'Adviser')).toBe(false);
  });

  it('ignores casing and surrounding space, as the mappings are hand-entered', () => {
    expect(matchesRole(['  adviser '], 'Adviser')).toBe(true);
  });
});
