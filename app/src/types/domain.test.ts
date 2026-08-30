import { describe, expect, it } from 'vitest';
import {
  CASE_STATUSES,
  CASE_STATUS_TRANSITIONS,
  canCorrectOutcome,
  canTransition,
  guardRegradeCase,
  guardSignOffRemediation,
  isValidCorrectionReason,
  nextStatuses,
  OUTCOMES,
  requiresRemediation,
  stageTone,
  type CaseStatus,
} from './domain';

describe('requiresRemediation (BR-006)', () => {
  it('requires remediation for every non-pass outcome', () => {
    expect(requiresRemediation('Pass with issues')).toBe(true);
    expect(requiresRemediation('Insufficient evidence')).toBe(true);
    expect(requiresRemediation('Potential harm')).toBe(true);
  });

  it('leaves a Pass alone', () => {
    expect(requiresRemediation('Pass')).toBe(false);
  });

  it('covers every outcome the solution may record', () => {
    // Guards the rule against a fifth outcome being added without a decision on it.
    expect(OUTCOMES.filter(requiresRemediation)).toHaveLength(OUTCOMES.length - 1);
  });
});

describe('canCorrectOutcome (AD-031)', () => {
  it('admits the owning role and the platform escalations', () => {
    expect(canCorrectOutcome('T&C Manager')).toBe(true);
    expect(canCorrectOutcome('Outcome Testing Manager')).toBe(true);
    expect(canCorrectOutcome('Administrator')).toBe(true);
  });

  it('refuses the roles that perform the work being corrected', () => {
    expect(canCorrectOutcome('AQS Reviewer')).toBe(false);
    expect(canCorrectOutcome('Tax Reviewer')).toBe(false);
    expect(canCorrectOutcome('Adviser')).toBe(false);
    expect(canCorrectOutcome('')).toBe(false);
  });
});

describe('isValidCorrectionReason (BR-012, NFR-AUD-01)', () => {
  it('accepts a reason with content', () => {
    expect(isValidCorrectionReason('Evidence supplied after the original check')).toBe(true);
  });

  it('rejects blank and whitespace-only reasons', () => {
    expect(isValidCorrectionReason('')).toBe(false);
    expect(isValidCorrectionReason('   \t\n ')).toBe(false);
  });
});

describe('guardRegradeCase (OD-007, AD-031)', () => {
  it('allows the T&C Manager with a reason', () => {
    expect(guardRegradeCase({ role: 'T&C Manager', reason: 'Regraded on appeal' })).toEqual({
      allowed: true,
    });
  });

  it('refuses a role that may not correct an outcome, and says who may', () => {
    const result = guardRegradeCase({ role: 'AQS Reviewer', reason: 'Regraded on appeal' });

    expect(result.allowed).toBe(false);
    expect(result.reason).toContain('T&C Manager');
  });

  it('refuses a permitted role with no reason', () => {
    const result = guardRegradeCase({ role: 'T&C Manager', reason: '  ' });

    expect(result.allowed).toBe(false);
    expect(result.reason).toContain('reason');
  });
});

describe('guardSignOffRemediation (BR-008, FR-023)', () => {
  it('allows an approval without notes', () => {
    expect(
      guardSignOffRemediation({ role: 'T&C Manager', decision: 'Approved', notes: '' }),
    ).toEqual({ allowed: true });
  });

  it('requires notes on a rejection, because the remediation returns with them', () => {
    const result = guardSignOffRemediation({
      role: 'T&C Manager',
      decision: 'Rejected',
      notes: '   ',
    });

    expect(result.allowed).toBe(false);
    expect(result.reason).toContain('notes');
  });

  it('allows a rejection that carries notes', () => {
    expect(
      guardSignOffRemediation({
        role: 'T&C Manager',
        decision: 'Rejected',
        notes: 'The suitability report still omits the charges comparison.',
      }),
    ).toEqual({ allowed: true });
  });

  it('refuses a role that does not own sign-off', () => {
    expect(
      guardSignOffRemediation({ role: 'Adviser', decision: 'Approved', notes: '' }).allowed,
    ).toBe(false);
  });
});

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

  it('returns a rejected sign-off to remediation', () => {
    expect(canTransition('Awaiting Sign-off', 'Awaiting Remediation')).toBe(true);
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
