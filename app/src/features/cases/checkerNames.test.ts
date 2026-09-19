import { describe, expect, it } from 'vitest';
import { CHECKER_LABELS, checkerLabel, checkerState, routeRequires } from './checkerNames';

/**
 * The Tax Checker and the AQS Checker on a case header (item 2, 2026-09-19).
 *
 * The server side of this rule — which column each discipline stamps — is mirrored in
 * CheckerNamesTests.cs. What is tested here is the part the server has no opinion about:
 * which of the three states a case is in, and what each one reads as.
 */

describe('routeRequires', () => {
  it('asks for both checks on a Tax then AQS case', () => {
    expect(routeRequires('Tax then AQS', 'Tax')).toBe(true);
    expect(routeRequires('Tax then AQS', 'AQS')).toBe(true);
  });

  it('asks for only the Tax check on a Tax only case', () => {
    expect(routeRequires('Tax only', 'Tax')).toBe(true);
    expect(routeRequires('Tax only', 'AQS')).toBe(false);
  });

  it('asks for only the AQS check on an AQS only case', () => {
    expect(routeRequires('AQS only', 'Tax')).toBe(false);
    expect(routeRequires('AQS only', 'AQS')).toBe(true);
  });

  it('treats an unknown route as requiring both', () => {
    // Every case created before the route seed existed. Showing a check that may not be
    // needed is the safe direction; hiding one that is needed is not.
    expect(routeRequires(null, 'Tax')).toBe(true);
    expect(routeRequires(null, 'AQS')).toBe(true);
  });
});

describe('checkerState', () => {
  it('names the checker where one is allocated', () => {
    expect(checkerState('Ada Checker', 'Tax then AQS', 'Tax')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });

  it('trims a stamped name', () => {
    expect(checkerState('  Ada Checker  ', 'Tax then AQS', 'AQS')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });

  it('reads an empty column on a required check as not yet allocated', () => {
    expect(checkerState(null, 'Tax then AQS', 'Tax')).toEqual({ kind: 'unallocated' });
    expect(checkerState('', 'Tax then AQS', 'AQS')).toEqual({ kind: 'unallocated' });
    expect(checkerState('   ', 'Tax then AQS', 'Tax')).toEqual({ kind: 'unallocated' });
  });

  it('reads an empty column on a check the case does not take as not required', () => {
    // The distinction the batch asked for. Both are an empty column, and they mean opposite
    // things: a Tax only case has not been overlooked for AQS, it simply owes no AQS check.
    expect(checkerState(null, 'Tax only', 'AQS')).toEqual({ kind: 'not-required' });
    expect(checkerState(null, 'AQS only', 'Tax')).toEqual({ kind: 'not-required' });
  });

  it('names a checker even on a discipline the route does not ask for', () => {
    // The data wins over the route. A name in the column means somebody was allocated, and
    // hiding it because the route disagrees would hide the more surprising fact.
    expect(checkerState('Ada Checker', 'Tax only', 'AQS')).toEqual({
      kind: 'named',
      name: 'Ada Checker',
    });
  });
});

describe('checkerLabel', () => {
  it('reads as the name where there is one', () => {
    expect(checkerLabel('Ada Checker', 'Tax then AQS', 'Tax')).toBe('Ada Checker');
  });

  it('distinguishes the two empty states in words', () => {
    expect(checkerLabel(null, 'Tax then AQS', 'AQS')).toBe(CHECKER_LABELS.unallocated);
    expect(checkerLabel(null, 'Tax only', 'AQS')).toBe(CHECKER_LABELS['not-required']);
    expect(CHECKER_LABELS.unallocated).not.toBe(CHECKER_LABELS['not-required']);
  });
});
