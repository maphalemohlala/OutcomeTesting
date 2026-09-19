import { describe, expect, it } from 'vitest';
import { adviceDateRefusal, dueDateRefusal } from './caseHeaderDates';
import { validateSubmit } from './caseEditSubmit';
import { can, DEFAULT_PERMISSIONS, resolvePermissions } from '../../types/permissions';

/**
 * The due date (item 6, 2026-09-19): three days after the upload, editable, and editable by
 * managers only.
 *
 * The client mirror of `DueDateRulesTests`. Neither tier is the boundary on its own - the
 * command re-checks `case.duedate` and re-runs the date rule - but the two have to agree,
 * because a panel that offers a field the server refuses loses the whole save.
 */

describe('who may move a due date', () => {
  function held(role: string): boolean {
    return can(resolvePermissions([role], DEFAULT_PERMISSIONS), 'case.duedate', 'Edit');
  }

  it('grants it to the two manager roles', () => {
    expect(held('AL Portal - T&C Supervisor')).toBe(true);
    expect(held('AL Portal - Outcome Testing Manager')).toBe(true);
  });

  it('keeps it from the checkers, who hold page.cases Edit for everything else', () => {
    // The reason it is its own key. Tax and AQS reviewers complete the header fields the IO
    // extract does not carry (project owner, 2026-09-12), so raising the bar on page.cases
    // would have taken the whole header away from the people meant to fill it in.
    for (const role of ['AL Portal - Tax Reviewer', 'AL Portal - AQS Reviewer']) {
      expect(can(resolvePermissions([role], DEFAULT_PERMISSIONS), 'page.cases', 'Edit')).toBe(true);
      expect(held(role)).toBe(false);
    }
  });

  it('keeps it from advisers and planners', () => {
    expect(held('AL Portal - Adviser Remediation')).toBe(false);
    expect(held('AL Portal - Planner')).toBe(false);
  });

  it('leaves it with the break-glass administrator and not the portal one', () => {
    // Portal Administrator holds page.cases at View, so it cannot reach the command at all;
    // a grant it could never exercise would read as an authority somebody has.
    expect(held('Administrators')).toBe(true);
    expect(held('AL Portal - Portal Administrator')).toBe(false);
  });
});

describe('dueDateRefusal', () => {
  it('accepts a deadline after the meeting', () => {
    expect(dueDateRefusal('2026-09-25', '2026-09-10')).toBeNull();
  });

  it('accepts a deadline on the day of the meeting', () => {
    expect(dueDateRefusal('2026-09-10', '2026-09-10')).toBeNull();
  });

  it('refuses a deadline moved back before the meeting, naming both', () => {
    expect(dueDateRefusal('2026-09-05', '2026-09-10')).toBe(
      'Due date cannot be earlier than Date of meeting - Client contact (10 Sep 2026).',
    );
  });

  it('words the refusal exactly as the server does', () => {
    // CaseHeaderRules.ValidateDueDate builds the same sentence with "d MMM yyyy". Two
    // messages for one rule is two rules as far as anyone reading them is concerned.
    expect(dueDateRefusal('2026-01-05', '2026-01-10')).toContain('(10 Jan 2026)');
  });

  it('ignores a time of day on the stored due date', () => {
    // al_duedate carries the upload's time, so the app may hold it as a timestamp.
    expect(dueDateRefusal('2026-09-10T09:00:00Z', '2026-09-10')).toBeNull();
  });

  it('says nothing about a meeting in the future', () => {
    // adviceDateRefusal owns that message, and it is the right one only when the user is
    // editing the meeting date.
    expect(dueDateRefusal('2099-01-01', '2098-01-01')).toBeNull();
    expect(adviceDateRefusal('2098-01-01', '2026-09-19')).toContain('cannot be in the future');
  });

  it('passes when either date is empty', () => {
    expect(dueDateRefusal('', '2026-09-10')).toBeNull();
    expect(dueDateRefusal('2026-09-10', '')).toBeNull();
    expect(dueDateRefusal(null, null)).toBeNull();
  });
});

describe('validateSubmit, with the due date now editable', () => {
  it('refuses a due date pulled back under the meeting already on the case', () => {
    const errors = validateSubmit({
      changedCount: 1,
      allocationCount: 0,
      dueDateChanged: '2026-09-05',
      adviceDateOnRecord: '2026-09-10',
    });

    expect(errors).toEqual([
      'Due date cannot be earlier than Date of meeting - Client contact (10 Sep 2026).',
    ]);
  });

  it('leaves a case whose stored dates already disagree editable', () => {
    // Nothing recalculates existing cases, so a case imported before this rule may hold a
    // pair that breaks it. Refusing an unrelated edit over that would make it unfixable.
    expect(
      validateSubmit({
        changedCount: 1,
        allocationCount: 0,
        dueDateChanged: null,
        adviceDateOnRecord: '2026-09-10',
        dueDate: '2026-09-05',
      }),
    ).toEqual([]);
  });

  it('compares against a meeting date typed in the same save', () => {
    // Both dates moving at once: the pair being written is what must agree, not one of them
    // against the value it is replacing.
    expect(
      validateSubmit({
        changedCount: 2,
        allocationCount: 0,
        adviceDate: '2026-09-18',
        dueDateChanged: '2026-09-25',
        adviceDateOnRecord: '2026-01-01',
      }),
    ).toEqual([]);
  });

  it('still reports a meeting in the future first', () => {
    // One message, and it is the one worth reading: a date beyond today is usually a typo in
    // the year, and naming the due date would send the user to the wrong field.
    const errors = validateSubmit({
      changedCount: 2,
      allocationCount: 0,
      adviceDate: '2099-01-01',
      dueDateChanged: '2026-09-25',
    });

    expect(errors).toHaveLength(1);
    expect(errors[0]).toContain('cannot be in the future');
  });
});
