import { describe, expect, it } from 'vitest';
import { remediationByCase, type CaseRemediationSource } from './caseRemediation';

const OPEN = 120910600;
const COMPLETED = 120910602;

function action(
  id: string,
  caseId: string | undefined,
  status: number,
  name?: string,
): CaseRemediationSource {
  return {
    al_remediationactionid: id,
    _al_outcomecaseid_value: caseId,
    al_actionstatus: status,
    al_actionstatusname: name,
  };
}

describe('remediationByCase', () => {
  it('reads a case as complete only when every action on it is completed', () => {
    // The sign-off rule: a remediation is approved as a whole. TEST on 2026-09-28 held 84
    // open actions on 11 cases - the worklist has to answer in cases, and one open action
    // keeps its case open.
    const byCase = remediationByCase([
      action('a1', 'case-1', COMPLETED, 'Completed'),
      action('a2', 'case-1', OPEN, 'Open'),
      action('a3', 'case-2', COMPLETED, 'Completed'),
      action('a4', 'case-2', COMPLETED, 'Completed'),
    ]);

    expect(byCase.get('case-1')).toEqual({ state: 'open', open: 1, total: 2 });
    expect(byCase.get('case-2')).toEqual({ state: 'complete', open: 0, total: 2 });
  });

  it('falls back to the option-set value when the formatted status is absent', () => {
    // getAll does not always return the *name formatted value. Reading the name alone would
    // call every completed action open.
    const byCase = remediationByCase([action('a1', 'case-1', COMPLETED)]);

    expect(byCase.get('case-1')?.state).toBe('complete');
  });

  it('leaves out an action that names no case', () => {
    const byCase = remediationByCase([action('a1', undefined, OPEN, 'Open')]);

    expect(byCase.size).toBe(0);
  });
});
