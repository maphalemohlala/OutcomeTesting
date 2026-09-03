import { describe, expect, it } from 'vitest';
import { toOutcome } from './caseOutcomeMapping';
import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

/**
 * The recheck screen offers to overwrite a grade, so a row it renders wrongly is worse
 * than one it refuses to render. These cover the two ways that happens: a missing
 * formatted label, and an ungraded outcome read as though it had been regraded.
 */
function record(overrides: Partial<Al_outcomes>): Al_outcomes {
  return {
    al_outcomeid: 'outcome-1',
    al_outcomecode: 'OUT-0001',
    al_name: '',
    al_initialoutcome: 120910701,
    ...overrides,
  } as Al_outcomes;
}

describe('toOutcome', () => {
  it('labels both grades from their numeric values when no formatted name is returned', () => {
    const outcome = toOutcome(record({ al_finaloutcome: 120910712 }));

    expect(outcome.initialOutcome).toBe('Pass with issues');
    expect(outcome.finalOutcome).toBe('Insufficient evidence');
  });

  it('prefers the formatted name when Dataverse supplies one', () => {
    const outcome = toOutcome(
      record({
        al_initialoutcomename: 'Pass with issues',
        al_finaloutcome: 120910710,
        al_finaloutcomename: 'Pass',
      }),
    );

    expect(outcome.initialOutcome).toBe('Pass with issues');
    expect(outcome.finalOutcome).toBe('Pass');
  });

  it('reports an ungraded outcome as null rather than falling back to the initial grade', () => {
    // BR-007 keeps both grades apart. Defaulting the final grade to the initial one would
    // make an untouched outcome look regraded, and the screen would offer to "correct" it
    // to the value it already shows.
    const outcome = toOutcome(record({}));

    expect(outcome.finalOutcome).toBeNull();
    expect(outcome.initialOutcome).toBe('Pass with issues');
  });

  it('carries the row version as a string for the concurrency check', () => {
    // ExpectedRowVersion is a string parameter on the command; a numeric 0 must survive
    // as "0" rather than being dropped as falsy, or the check is silently skipped.
    expect(toOutcome(record({ versionnumber: 0 })).rowVersion).toBe('0');
    expect(toOutcome(record({ versionnumber: 4821 })).rowVersion).toBe('4821');
    expect(toOutcome(record({})).rowVersion).toBeNull();
  });

  it('falls back to the outcome code when the row has no name', () => {
    expect(toOutcome(record({ al_name: '   ' })).reference).toBe('OUT-0001');
    expect(toOutcome(record({ al_name: 'Regraded outcome' })).reference).toBe('Regraded outcome');
  });

  it('treats a blank regrade reason as absent', () => {
    expect(toOutcome(record({ al_regradereason: '  ' })).regradeReason).toBeNull();
    expect(toOutcome(record({ al_regradereason: 'Wrong evidence read' })).regradeReason).toBe(
      'Wrong evidence read',
    );
  });
});
