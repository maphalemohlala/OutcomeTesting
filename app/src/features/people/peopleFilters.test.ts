import { describe, expect, it } from 'vitest';
import { matchesRole } from './peopleFilters';

/**
 * The filter compares web role NAMES on both sides.
 *
 * Until 2026-09-22 it compared the six `al_Role` labels from `data/roles-seed` against the
 * web role names `al_userrolemapping` actually carries. The two lists share no member, so
 * every specific role matched nobody: DEV showed "0 of 12 people" for all six. These use
 * real live names so the vocabulary is pinned to what the server holds, not to a constant
 * this module could redefine.
 */
const LIVE_ROLES = [
  'AL Portal - Adviser Remediation',
  'AL Portal - Planner',
  'AL Portal - T&C Supervisor',
  'AL Portal - Tax Reviewer',
  'AL Portal - AQS Reviewer',
] as const;

describe('People role filter', () => {
  it('matches a web role name as the mappings carry it', () => {
    expect(matchesRole(['AL Portal - Planner'], 'AL Portal - Planner')).toBe(true);
  });

  it('does not match the retired al_Role label against a web role name', () => {
    // The regression, stated directly: "Paraplanner" is not what anybody holds.
    expect(matchesRole(['AL Portal - Planner'], 'Paraplanner')).toBe(false);
    expect(matchesRole(['AL Portal - Adviser Remediation'], 'Adviser')).toBe(false);
  });

  it('matches anyone holding the role, not only those holding it alone', () => {
    // A person can hold several (D8) - a T&C Manager who also advises is one person, not a
    // reason to show them twice or to hide them from either filter.
    const held = ['AL Portal - T&C Supervisor', 'AL Portal - Adviser Remediation'];
    expect(matchesRole(held, 'AL Portal - Adviser Remediation')).toBe(true);
    expect(matchesRole(held, 'AL Portal - T&C Supervisor')).toBe(true);
  });

  it('lets everyone through when no role is chosen', () => {
    expect(matchesRole([], 'all')).toBe(true);
    expect(matchesRole(['AL Portal - Planner'], 'all')).toBe(true);
  });

  it('excludes someone who holds no roles when a role is chosen', () => {
    expect(matchesRole([], 'AL Portal - Planner')).toBe(false);
  });

  it('ignores casing and surrounding space, as the mappings are hand-entered', () => {
    expect(matchesRole(['  al portal - planner '], 'AL Portal - Planner')).toBe(true);
  });

  it('matches every role the live list offers, so no option is inert', () => {
    // The shape of the original defect was an option that could never match anything. This
    // fails if any offered role stops being matchable.
    for (const role of LIVE_ROLES) {
      expect(matchesRole([role], role)).toBe(true);
    }
  });
});
