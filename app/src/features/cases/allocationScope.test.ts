import { describe, expect, it } from 'vitest';
import {
  AQS_TEAM_MANAGER,
  TAX_TEAM_MANAGER,
  adviserUnmatched,
  allocatableDisciplines,
} from './allocationScope';

/** Mirrors AllocationScope in the plug-in assembly (AD-218). The command is the authority. */
describe('allocatableDisciplines', () => {
  it.each([
    [[TAX_TEAM_MANAGER], ['Tax']],
    [[AQS_TEAM_MANAGER], ['AQS']],
    [['AL Portal - Outcome Testing Manager'], ['Tax', 'AQS']],
    [['Administrators'], ['Tax', 'AQS']],
    [['AL Portal - T&C Supervisor'], []],
    [[TAX_TEAM_MANAGER, AQS_TEAM_MANAGER], ['Tax', 'AQS']],
  ])('%j allocates %j', (roles, expected) => {
    expect(allocatableDisciplines(roles)).toEqual(expected);
  });

  it('matches role names whatever their case and spacing', () => {
    expect(allocatableDisciplines(['  al portal - tax team manager '])).toEqual(['Tax']);
  });
});

describe('adviserUnmatched', () => {
  it('flags a case in remediation with nobody to release it to', () => {
    expect(adviserUnmatched(120910587, null)).toBe(true);
  });

  it('does not flag a matched case, a closed case or a case still in review', () => {
    expect(adviserUnmatched(120910587, 'x')).toBe(false);
    expect(adviserUnmatched(120910591, null)).toBe(false);
    expect(adviserUnmatched(120910584, null)).toBe(false);
  });
});
