import { readFileSync } from 'node:fs';
import { describe, expect, it } from 'vitest';
import { PROTECTED_QUESTION_CODES, protectedReason } from './protectedQuestions';

/**
 * Mirrors ChecklistGuards in plugins/OutcomeTesting.Plugins, which is authoritative: this
 * copy only hides a control the server would refuse anyway (AD-041).
 */
describe('protectedQuestions', () => {
  it('carries exactly the eight codes the plug-in assembly guards', () => {
    expect([...PROTECTED_QUESTION_CODES].sort()).toEqual(
      [
        'Q-FQ-01',
        'Q-FQ-02',
        'Q-FQ-03',
        'Q-FQTAX-01',
        'Q-FQTAX-02',
        'Q-FQTAX-03',
        'Q-GR-01',
        'Q-TAX-02',
      ].sort(),
    );
  });

  it('explains what a protected code is needed for', () => {
    expect(protectedReason('Q-GR-01')).toContain('grade');
  });

  it('returns null for an ordinary question', () => {
    expect(protectedReason('Q-E1-01')).toBeNull();
  });

  it('matches without regard to case', () => {
    expect(protectedReason('q-fq-01')).not.toBeNull();
  });

  it('returns null for a missing code', () => {
    expect(protectedReason(undefined)).toBeNull();
    expect(protectedReason('')).toBeNull();
  });
});

describe('the mirror matches the plug-in assembly', () => {
  it('guards the same codes ChecklistGuards does', () => {
    // Read from the C# rather than restated, because a hand-copied list goes stale
    // silently: a ninth code added server-side would leave the UI offering a Retire
    // control the server refuses.
    const csharp = readFileSync(
      new URL('../../../../plugins/OutcomeTesting.Plugins/ChecklistGuards.cs', import.meta.url),
      'utf8',
    );
    const inCsharp = [...csharp.matchAll(/\{ "(Q-[A-Z0-9-]+)",/g)].map((m) => m[1]).sort();

    expect(inCsharp).toHaveLength(8);
    expect(inCsharp).toEqual([...PROTECTED_QUESTION_CODES].sort());
  });
});
