import { describe, expect, it } from 'vitest';

import { groupIssues, splitIssues } from './remediationIssues';
import type { RemediationActionRow } from './remediationMapping';

/** The description Remediation.Describe writes for a Tax review that did not pass. */
const DESCRIPTION = [
  'Issues found on the check:',
  '- Tax check outcome: Insufficient evidence',
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
      'Tax check outcome: Insufficient evidence',
      'Fail point: AML - No CRA completed or missing data fields',
      'Fail point: AML - CRA highlighted a High Risk, with no supporting form completed',
      'Fail point: Record Keeping - Concession required but not on file',
      'Fail point: Record Keeping - No client agreement or client acceptance in place',
    ]);
  });

  it('keeps the hyphens inside an item and strips only the bullet', () => {
    expect(splitIssues('- AML - ID verification issue').issues).toEqual([
      'AML - ID verification issue',
    ]);
  });

  it('leaves the checker note and the standing sentence as the note, without the heading', () => {
    expect(splitIssues(DESCRIPTION).note).toBe(
      'The checker recorded: passed\n\n' +
        'Raised automatically when the review was submitted (Tax check: Insufficient evidence).\n' +
        'Review the file and record what you have put right.',
    );
  });

  it('reads a description written with Windows line endings', () => {
    const crlf = 'Issues found on the check:\r\n- One\r\n- Two\r\n\r\nA note.';
    expect(splitIssues(crlf)).toEqual({ issues: ['One', 'Two'], note: 'A note.' });
  });

  it('leaves a description with no items whole, as the note', () => {
    const plain = 'Raised automatically when the review was submitted. Review the file.';
    expect(splitIssues(plain)).toEqual({ issues: [], note: plain });
  });

  it('reads an item list with no checker observation behind it', () => {
    const noObservation = 'Issues found on the check:\n- One\n\nRaised automatically.';
    expect(splitIssues(noObservation)).toEqual({
      issues: ['One'],
      note: 'Raised automatically.',
    });
  });

  it('has nothing to show for an absent or blank description', () => {
    expect(splitIssues(null)).toEqual({ issues: [], note: null });
    expect(splitIssues(undefined)).toEqual({ issues: [], note: null });
    expect(splitIssues('   ')).toEqual({ issues: [], note: null });
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
  };
}

describe('groupIssues', () => {
  it('gives every item its own numbered line', () => {
    const [group] = groupIssues([action('a', DESCRIPTION)]);
    expect(group.lines).toHaveLength(5);
    expect(group.lines[0]).toEqual({
      number: 1,
      issue: 'Tax check outcome: Insufficient evidence',
    });
    expect(group.lines[4].number).toBe(5);
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
    expect(group.lines.some((line) => line.issue.includes('Raised automatically'))).toBe(false);
    expect(group.note).toContain('Raised automatically');
  });

  it('shows an action with no item list as one row, and adds no note under it', () => {
    const plain = 'Raised automatically when the review was submitted. Review the file.';
    const [group] = groupIssues([action('a', plain)]);
    expect(group.lines).toEqual([{ number: 1, issue: plain }]);
    expect(group.note).toBeNull();
  });

  it('still gives an action with no description at all a row', () => {
    const [group] = groupIssues([action('a', '—')]);
    expect(group.lines).toEqual([{ number: 1, issue: '—' }]);
  });
});
