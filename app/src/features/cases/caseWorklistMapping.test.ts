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

describe('toSummary vulnerable client and CAS status (completed report)', () => {
  it('reads the managed-list answer first, then the choice an un-backfilled case holds', () => {
    const managed = record({
      al_vulnerableclient: 120910551,
      ['_al_vulnerableclientid_value@OData.Community.Display.V1.FormattedValue' as never]: 'Potentially vulnerable',
    } as Partial<Al_outcomecases>);
    expect(toSummary(managed).vulnerableClient).toBe('Potentially vulnerable');
    expect(toSummary(record({ al_vulnerableclient: 120910550 })).vulnerableClient).toBe('Yes');
    expect(toSummary(record({})).vulnerableClient).toBeNull();
  });

  it('labels the adviser status, which is the CAS status', () => {
    expect(toSummary(record({ al_adviserstatus: 120910501 })).adviserStatus).toBe('CAS');
    expect(toSummary(record({})).adviserStatus).toBeNull();
  });
});
