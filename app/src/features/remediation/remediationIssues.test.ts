import { describe, expect, it } from 'vitest';

import { groupIssues, outcomeOf, splitIssues } from './remediationIssues';
import type { RemediationActionRow } from './remediationMapping';

/**
 * The description Remediation.Describe writes for a Tax review that did not pass.
 *
 * The Tax check outcome is deliberately not one of the items: it is the result the items
 * are reasons for, and it is carried in the standing sentence's brackets instead.
 */
const DESCRIPTION = [
  'Issues found on the check:',
  '- Fail point: AML - No CRA completed or missing data fields',
  '- Fail point: AML - CRA highlighted a High Risk, with no supporting form completed',
  '- Fail point: Record Keeping - Concession required but not on file',
  '- Fail point: Record Keeping - No client agreement or client acceptance in place',
  '',
  'The checker recorded: passed',
  '',
  'Raised automatically when the review was submitted (Tax check: Insufficient evidence).',
  'Review the file and record what you have put right.',
].join('\n');

describe('splitIssues', () => {
  it('gives one issue per item the checker marked down', () => {
    expect(splitIssues(DESCRIPTION).issues).toEqual([
      'Fail point: AML - No CRA completed or missing data fields',
      'Fail point: AML - CRA highlighted a High Risk, with no supporting form completed',
      'Fail point: Record Keeping - Concession required but not on file',
      'Fail point: Record Keeping - No client agreement or client acceptance in place',
    ]);
  });

  it('reads the outcome out of the standing sentence', () => {
    expect(splitIssues(DESCRIPTION).outcome).toBe('Tax check: Insufficient evidence');
  });

  it('reads an AQS grade, which is the reason written without a prefix', () => {
    const graded = [
      'Issues found on the check:',
      '- Fail point: AML - ID verification issue',
      '',
      'Raised automatically when the review was submitted (Potential harm).',
      'Review the file and record what you have put right.',
    ].join('\n');
    expect(splitIssues(graded).outcome).toBe('Potential harm');
  });

  it('takes the sentence brackets, not brackets the checker typed', () => {
    const noisy = [
      'Issues found on the check:',
      '- One',
      '',
      'The checker recorded: no CRA on file (see IO-1234) and no TOB',
      '',
      'Raised automatically when the review was submitted (Fail).',
      'Review the file and record what you have put right.',
    ].join('\n');
    expect(splitIssues(noisy).outcome).toBe('Fail');
  });

  it('names no outcome when the sentence carries no reason', () => {
    const noReason = [
      'Issues found on the check:',
      '- One',
      '',
      'Raised automatically when the review was submitted. Review the file.',
    ].join('\n');
    expect(splitIssues(noReason).outcome).toBeNull();
  });

  it('names no outcome from a checker note alone, the marker being absent', () => {
    expect(splitIssues('The checker recorded: see file (IO-1234)').outcome).toBeNull();
  });

  it('keeps the hyphens inside an item and strips only the bullet', () => {
    expect(splitIssues('- AML - ID verification issue').issues).toEqual([
      'AML - ID verification issue',
    ]);
  });

  it('keeps nothing but the items - no heading, observation or standing sentence', () => {
    // None of the three is drawn (project owner, 2026-09-10). The observation went with the
    // rest when the table was cut back to the issues themselves, and what the description
    // says about why it was raised is the Outcome above the table.
    const { issues } = splitIssues(DESCRIPTION);

    expect(issues.every((issue) => issue.startsWith('Fail point:'))).toBe(true);
    expect(issues.join(' ')).not.toContain('The checker recorded');
    expect(issues.join(' ')).not.toContain('Raised automatically');
  });

  it('reads a description written with Windows line endings', () => {
    const crlf = 'Issues found on the check:\r\n- One\r\n- Two\r\n\r\nA note.';
    expect(splitIssues(crlf)).toEqual({ issues: ['One', 'Two'], outcome: null });
  });

  it('finds no items in a description the plug-in did not write', () => {
    expect(splitIssues('Please re-check the client agreement.')).toEqual({
      issues: [],
      outcome: null,
    });
  });

  it('reads an item list with no checker observation behind it', () => {
    const noObservation = 'Issues found on the check:\n- One\n\nRaised automatically.';
    expect(splitIssues(noObservation)).toEqual({ issues: ['One'], outcome: null });
  });

  it('has nothing to show for an absent or blank description', () => {
    expect(splitIssues(null)).toEqual({ issues: [], outcome: null });
    expect(splitIssues(undefined)).toEqual({ issues: [], outcome: null });
    expect(splitIssues('   ')).toEqual({ issues: [], outcome: null });
  });

  it('drops a bullet with nothing behind it', () => {
    expect(splitIssues('Issues found on the check:\n- \n- Real one').issues).toEqual(['Real one']);
  });
});

function action(id: string, description: string): RemediationActionRow {
  return {
    id,
    reference: id,
    description,
    status: 'Open',
    dueOn: null,
    completedOn: null,
    triggeredBy: null,
    owner: null,
    rowVersion: null,
    remedialAction: null,
    evidenceReference: null,
    clientContactRequired: null,
    recheckRequired: null,
    changesAdvice: null,
    assignedTo: null,
    createdOn: null,
    clockStartedOn: null,
    completedOnRaw: null,
  };
}

describe('groupIssues', () => {
  it('gives every item its own numbered line', () => {
    const [group] = groupIssues([action('a', DESCRIPTION)]);
    expect(group.lines).toHaveLength(4);
    expect(group.lines[0]).toEqual({
      number: 1,
      issue: 'Fail point: AML - No CRA completed or missing data fields',
    });
    expect(group.lines[3].number).toBe(4);
  });

  it('numbers straight through, so a second action carries on from the first', () => {
    const groups = groupIssues([
      action('a', ['Issues found on the check:', '- One', '- Two'].join('\n')),
      action('b', ['Issues found on the check:', '- Three'].join('\n')),
    ]);
    expect(groups[0].lines.map((line) => line.number)).toEqual([1, 2]);
    expect(groups[1].lines.map((line) => line.number)).toEqual([3]);
    expect(groups[1].lines[0].issue).toBe('Three');
  });

  it('keeps the context off the rows entirely', () => {
    const [group] = groupIssues([action('a', DESCRIPTION)]);
    const drawn = group.lines.map((line) => line.issue).join(' ');

    expect(drawn).not.toContain('The checker recorded');
    expect(drawn).not.toContain('Raised automatically');
  });

  it('shows an action with no item list as one row, and adds no note under it', () => {
    // What dropoutcomeactions leaves when it strips a case's only action: the checker's
    // words become the row, because there is no item to number.
    const stripped = [
      'The checker recorded: failed',
      '',
      'Raised automatically when the review was submitted (Tax check: Fail). Review the file.',
    ].join('\n');
    const [group] = groupIssues([action('a', stripped)]);
    expect(group.lines).toEqual([{ number: 1, issue: '—' }]);
  });

  it('falls back to a dash when the description says nothing of its own', () => {
    const standing = 'Raised automatically when the review was submitted. Review the file.';
    const [group] = groupIssues([action('a', standing)]);
    expect(group.lines).toEqual([{ number: 1, issue: '—' }]);
  });

  it('still gives an action with no description at all a row', () => {
    const [group] = groupIssues([action('a', '—')]);
    expect(group.lines).toEqual([{ number: 1, issue: '—' }]);
  });
});

describe('outcomeOf', () => {
  it('reads the outcome the actions were raised for', () => {
    expect(outcomeOf([action('a', DESCRIPTION), action('b', DESCRIPTION)])).toBe(
      'Tax check: Insufficient evidence',
    );
  });

  it('names both when a Tax and an AQS review each raised remediation', () => {
    const raisedFor = (reason: string) =>
      [
        'Issues found on the check:',
        '- One',
        '',
        'Raised automatically when the review was submitted (' + reason + ').',
      ].join('\n');

    expect(
      outcomeOf([
        action('a', raisedFor('Tax check: Insufficient evidence')),
        action('b', raisedFor('Fail')),
      ]),
    ).toBe('Tax check: Insufficient evidence; Fail');
  });

  it('has no outcome to give for actions that name none', () => {
    expect(outcomeOf([])).toBeNull();
    expect(
      outcomeOf([action('a', ['Issues found on the check:', '- One'].join('\n'))]),
    ).toBeNull();
  });
});
