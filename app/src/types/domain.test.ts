import { describe, expect, it } from 'vitest';
import {
  CASE_STATUSES,
  CASE_STATUS_TRANSITIONS,
  canTransition,
  mayRegradeAt,
  nextStatuses,
  stageTone,
  type CaseStatus,
} from './domain';

describe('case lifecycle (BR-002, BR-008, AD-031, AD-036)', () => {
  it('walks the canonical path one step at a time', () => {
    const path: CaseStatus[] = [
      'Imported',
      'Ready for Allocation',
      'Queued',
      'Assigned',
      'Review In Progress',
      'Submitted',
      'Awaiting Remediation',
      'Remediation In Progress',
      'Awaiting Sign-off',
      'Awaiting Recheck',
      'Closed',
    ];

    for (let i = 0; i < path.length - 1; i += 1) {
      expect(canTransition(path[i], path[i + 1])).toBe(true);
    }
  });

  it('refuses the jump that would export an ungraded case', () => {
    // The export filters on Closed, so this would deliver a case with no review instance.
    expect(canTransition('Imported', 'Closed')).toBe(false);
  });

  it('closes a remediation that needs no sign-off, and only from Remediation In Progress', () => {
    // Pass with issues is not one the T&C Manager verifies; the server decides per case.
    expect(canTransition('Remediation In Progress', 'Closed')).toBe(true);
    expect(canTransition('Awaiting Remediation', 'Closed')).toBe(false);
  });

  it('returns a rejected sign-off to remediation', () => {
    expect(canTransition('Awaiting Sign-off', 'Awaiting Remediation')).toBe(true);
  });

  it('returns an approved sign-off to the queue when the AQS check is still owed', () => {
    // OD-038: the case goes through remediation before it can reach AQS, not instead of it.
    expect(canTransition('Awaiting Sign-off', 'Queued')).toBe(true);
  });

  it('reallocates a check the checker has already started', () => {
    // Project owner direction, 2026-09-10: managers reassign checkers after a review has
    // started. al_AssignCase stamps the review back to Assigned, so the case follows it.
    expect(canTransition('Review In Progress', 'Assigned')).toBe(true);
  });

  it('lets a corrected validation failure back into allocation', () => {
    expect(canTransition('Validation Failed', 'Ready for Allocation')).toBe(true);
  });

  it('offers the bypass only while no grade exists', () => {
    expect(canTransition('Queued', 'No Check Required')).toBe(true);
    expect(canTransition('Submitted', 'No Check Required')).toBe(false);
  });

  it('treats Closed and No Check Required as terminal', () => {
    expect(CASE_STATUS_TRANSITIONS.Closed).toHaveLength(0);
    expect(CASE_STATUS_TRANSITIONS['No Check Required']).toHaveLength(0);
    expect(canTransition('Closed', 'Review In Progress')).toBe(false);
  });

  it('always includes the current status, so an unchanged status is not a transition', () => {
    for (const status of CASE_STATUSES) {
      expect(nextStatuses(status)).toContain(status);
    }
  });

  it('offers every status when the case has none recorded', () => {
    expect(nextStatuses(null)).toEqual(CASE_STATUSES);
  });

  it('covers every status, so a new one cannot be added without a transition rule', () => {
    for (const status of CASE_STATUSES) {
      expect(CASE_STATUS_TRANSITIONS[status]).toBeDefined();
      expect(stageTone(status)).toBeDefined();
    }
  });

  it('only ever names statuses the model defines', () => {
    for (const targets of Object.values(CASE_STATUS_TRANSITIONS)) {
      for (const target of targets) {
        expect(CASE_STATUSES).toContain(target);
      }
    }
  });
});

describe('where a regrade is accepted', () => {
  it('is only at the recheck or once closed, or where no status is recorded', () => {
    const open = CASE_STATUSES.filter((status) => mayRegradeAt(status));
    expect(open).toEqual(['Awaiting Recheck', 'Closed']);
    expect(mayRegradeAt(null)).toBe(true);
  });
});

describe('where a check can be allocated', () => {
  it('never from a removed or closed case, since allocating moves it to Assigned', () => {
    expect(canTransition('No Check Required', 'Assigned')).toBe(false);
    expect(canTransition('Closed', 'Assigned')).toBe(false);
    expect(canTransition('Queued', 'Assigned')).toBe(true);
  });
});
