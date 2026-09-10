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

  it('leaves the checker’s own words as the note, and nothing else', () => {
    // The standing sentence is not shown (project owner, 2026-09-10): it said the same
    // thing under every row of every case, and both halves of it are on the page in their
    // own right now - the outcome as the remediation's Outcome, and what to do about it as
    // the row the adviser answers.
    expect(splitIssues(DESCRIPTION).note).toBe('The checker recorded: passed');
  });

  it('drops the standing sentence even when it arrives broken across lines', () => {
    const split = [
      'Raised automatically when the review was submitted (Tax check: Fail).',
      'Review the file and record what you have put right.',
    ].join('\n');

    expect(splitIssues(split).note).toBeNull();
  });

  it('reads a description written with Windows line endings', () => {
    const crlf = 'Issues found on the check:\r\n- One\r\n- Two\r\n\r\nA note.';
    expect(splitIssues(crlf)).toEqual({
      issues: ['One', 'Two'],
      note: 'A note.',
      outcome: null,
    });
  });

  it('leaves a description the plug-in did not write whole, as the note', () => {
    const plain = 'Please re-check the client agreement before responding.';
    expect(splitIssues(plain)).toEqual({ issues: [], note: plain, outcome: null });
  });

  it('has no note left when the description was only the standing sentence', () => {
    const standing = 'Raised automatically when the review was submitted. Review the file.';
    expect(splitIssues(standing).note).toBeNull();
  });

  it('reads an item list with no checker observation behind it', () => {
    const noObservation = 'Issues found on the check:\n- One\n\nRaised automatically.';
    expect(splitIssues(noObservation)).toEqual({
      issues: ['One'],
      note: 'Raised automatically.',
      outcome: null,
    });
  });

  it('has nothing to show for an absent or blank description', () => {
    expect(splitIssues(null)).toEqual({ issues: [], note: null, outcome: null });
    expect(splitIssues(undefined)).toEqual({ issues: [], note: null, outcome: null });
    expect(splitIssues('   ')).toEqual({ issues: [], note: null, outcome: null });
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

  it('keeps the context off the item rows, as the group note', () => {
    const [group] = groupIssues([action('a', DESCRIPTION)]);
    expect(group.lines.some((line) => line.issue.includes('The checker recorded'))).toBe(false);
    expect(group.note).toBe('The checker recorded: passed');
  });

  it('shows the shared context once across the actions a review raised together', () => {
    // One action per item, each carrying the same provenance in its own description.
    const item = (text: string) =>
      ['Issues found on the check:', `- ${text}`, '', 'The checker recorded: passed'].join('\n');
    const groups = groupIssues([action('a', item('One')), action('b', item('Two'))]);

    expect(groups[0].note).toBe('The checker recorded: passed');
    expect(groups[1].note).toBeNull();
  });

  it('shows the context again when the next action carries a different one', () => {
    const groups = groupIssues([
      action('a', ['Issues found on the check:', '- One', '', 'First context.'].join('\n')),
      action('b', ['Issues found on the check:', '- Two', '', 'Second context.'].join('\n')),
    ]);

    expect(groups[0].note).toBe('First context.');
    expect(groups[1].note).toBe('Second context.');
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
    expect(group.lines).toEqual([{ number: 1, issue: 'The checker recorded: failed' }]);
    expect(group.note).toBeNull();
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
