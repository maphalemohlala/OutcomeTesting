import { describe, expect, it } from 'vitest';
// Imported as text rather than read with node:fs, because tsconfig.app.json restricts
// ambient types to vite/client on purpose - app code must not reach for Node APIs that do
// not exist in a browser, and one test is not a reason to widen that.
import checklistGuardsSource from '../../../../plugins/OutcomeTesting.Plugins/ChecklistGuards.cs?raw';
import { PROTECTED_QUESTION_CODES, PROTECTED_QUESTION_PREFIXES, protectedReason } from './protectedQuestions';

/**
 * Mirrors ChecklistGuards in plugins/OutcomeTesting.Plugins, which is authoritative: this
 * copy only hides a control the server would refuse anyway (AD-041).
 */
describe('protectedQuestions', () => {
  it('carries exactly the codes the plug-in assembly guards', () => {
    expect([...PROTECTED_QUESTION_CODES].sort()).toEqual(
      [
        'Q-FQ-01',
        'Q-FQ-02',
        'Q-FQ-03',
        'Q-FQTAX-01',
        'Q-FQTAX-02',
        'Q-FQTAX-03',
        'Q-GR-01',
        'Q-GR-02',
        'Q-GR-03',
        'Q-GR-04',
        'Q-TAX-02',
        'Q-TAX-03',
        'Q-TAX-04',
        'Q-TAX-05',
        'Q-TAX-06',
        'Q-TAX-07',
        'Q-TAX-08',
        'Q-TAX-09',
        'Q-TAX-10',
        'Q-TAX-11',
        'Q-TAX-12',
      ].sort(),
    );
  });

  it('guards every AML checking point by the start of its code', () => {
    expect(protectedReason('Q-AML-01')).toBe(
      'Q-AML-01 is an AML checking point, which grading reads by the start of its code.',
    );
    expect(protectedReason('q-aml-05')).not.toBeNull();
    expect(protectedReason('Q-TAX-01')).toBeNull();
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
    // silently: a tenth code added server-side would leave the UI offering a Retire
    // control the server refuses.
    const inCsharp = [...checklistGuardsSource.matchAll(/\{ "(Q-[A-Z0-9-]+)",/g)]
      .map((m) => m[1])
      .sort();

    // The C# holds codes and prefixes as dictionary entries of the same shape.
    expect(inCsharp).toHaveLength(22);
    expect(inCsharp).toEqual([...PROTECTED_QUESTION_CODES, ...PROTECTED_QUESTION_PREFIXES].sort());
  });
});
