import { describe, expect, it } from 'vitest';
import { groupLetters } from './letterGroups';

const builtIn = (code: string) => ({ code, custom: false, eventValue: null });

describe('groupLetters', () => {
  it('puts the built-in letters under their stage, in case order', () => {
    const groups = groupLetters([
      builtIn('SIGNOFF-APPROVED-CLOSED'),
      builtIn('ALLOCATION'),
      builtIn('REMEDIATION-HARM'),
      builtIn('CASE-PASSED'),
      builtIn('ALLOCATION-NO-LINK'),
      builtIn('RECHECK-DUE'),
      builtIn('REVIEW-SUBMITTED'),
    ]);

    expect(groups.map((g) => [g.name, g.letters.map((l) => l.code)])).toEqual([
      ['Allocation', ['ALLOCATION', 'ALLOCATION-NO-LINK']],
      ['Review', ['CASE-PASSED', 'REVIEW-SUBMITTED']],
      ['Remediation', ['REMEDIATION-HARM']],
      ['Sign-off', ['SIGNOFF-APPROVED-CLOSED', 'RECHECK-DUE']],
    ]);
  });

  it('places a letter of your own by the event that sends it, and one with none under Other', () => {
    const groups = groupLetters([
      { code: 'TELL-THE-MANAGER', custom: true, eventValue: 120910803 },
      { code: 'NOTHING-SENDS-THIS', custom: true, eventValue: null },
    ]);

    expect(groups.map((g) => [g.name, g.letters.map((l) => l.code)])).toEqual([
      ['Sign-off', ['TELL-THE-MANAGER']],
      ['Other', ['NOTHING-SENDS-THIS']],
    ]);
  });

  it('leaves out a stage with no letters', () => {
    expect(groupLetters([builtIn('ALLOCATION')]).map((g) => g.name)).toEqual(['Allocation']);
  });
});
