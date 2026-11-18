import { describe, expect, it } from 'vitest';
import { toOutcome } from './caseOutcomeMapping';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

const base = { al_outcomeid: 'o1', al_outcomecode: 'OUT-1', al_initialoutcome: 120910701 } as Al_outcomes;

describe('the one outcome mapper', () => {
  it('reads an empty final grade as null, never undefined', () => {
    expect(toOutcome({ ...base, al_finaloutcome: null } as unknown as Al_outcomes).finalOutcome).toBeNull();
    expect(toOutcome(base).finalOutcome).toBeNull();
  });

  it('reads an empty row version as null, not the text "null"', () => {
    expect(toOutcome({ ...base, versionnumber: null } as unknown as Al_outcomes).rowVersion).toBeNull();
  });

  it('carries what the accountability editor and the summary need', () => {
    const outcome = toOutcome({
      ...base,
      al_regradedon: '2026-10-01T10:00:00Z',
      _al_fqaccountablecontactid_value: 'c1',
      '_al_fqaccountablecontactid_value@OData.Community.Display.V1.FormattedValue': 'Ann Adviser',
    } as unknown as Al_outcomes);
    expect(outcome.regraded).toBe(true);
    expect(outcome.fqAccountable).toEqual({ id: 'c1', name: 'Ann Adviser' });
    expect(outcome.aqAccountable).toBeNull();
    expect(outcome.accountability).toEqual({
      fqAdviser: false,
      fqParaplanner: false,
      aqAdviser: false,
      aqParaplanner: false,
    });
  });
});
