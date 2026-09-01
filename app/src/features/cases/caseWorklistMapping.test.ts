import { describe, expect, it } from 'vitest';
import { toSummary } from './caseWorklistMapping';
import type { Al_outcomecases } from '../../generated/models/Al_outcomecasesModel';

function record(overrides: Partial<Al_outcomecases>): Al_outcomecases {
  return {
    al_outcomecaseid: 'case-1',
    al_casereference: 'IO-100001',
    al_casestatus: 120910580,
    ...overrides,
  } as Al_outcomecases;
}

describe('toSummary priority', () => {
  it('labels priority from its numeric value when the formatted name is absent', () => {
    expect(toSummary(record({ al_priority: 120910782 })).priority).toBe('High');
    expect(toSummary(record({ al_priority: 120910780 })).priority).toBe('Low');
  });

  it('prefers the formatted name when Dataverse does supply it', () => {
    expect(
      toSummary(record({ al_priority: 120910782, al_priorityname: 'High (formatted)' })).priority,
    ).toBe('High (formatted)');
  });

  it('reports an unset priority as absent rather than guessing', () => {
    expect(toSummary(record({})).priority).toBeNull();
  });
});
