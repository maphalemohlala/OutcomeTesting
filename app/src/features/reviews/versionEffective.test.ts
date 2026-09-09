import { describe, expect, it } from 'vitest';
import { isVersionEffective } from './versionEffective';

/**
 * Mirrors ResponseRules.IsVersionEffective in plugins/OutcomeTesting.Plugins, which is the
 * authoritative rule. A retired question version keeps its answers (BR-013) but is not the
 * version a review is read against; the version effective on the reference day is.
 */
describe('isVersionEffective', () => {
  const asOf = new Date('2026-09-08T19:02:35Z');

  it('treats a version with no effective-to as current', () => {
    expect(isVersionEffective({}, asOf)).toBe(true);
    expect(isVersionEffective({ al_effectivefrom: '2026-08-26T00:00:00Z' }, asOf)).toBe(true);
  });

  it('retires a version from the start of its effective-to day', () => {
    expect(isVersionEffective({ al_effectiveto: '2026-09-08T00:00:00Z' }, asOf)).toBe(false);
    expect(
      isVersionEffective({ al_effectiveto: '2026-09-08T00:00:00Z' }, new Date('2026-09-08T00:00:00Z')),
    ).toBe(false);
  });

  it('keeps a version current the day before it retires', () => {
    expect(
      isVersionEffective({ al_effectiveto: '2026-09-08T00:00:00Z' }, new Date('2026-09-07T23:59:00Z')),
    ).toBe(true);
  });

  it('starts a successor from its effective-from day', () => {
    expect(isVersionEffective({ al_effectivefrom: '2026-09-08T00:00:00Z' }, asOf)).toBe(true);
    expect(
      isVersionEffective({ al_effectivefrom: '2026-09-08T00:00:00Z' }, new Date('2026-09-07T23:59:00Z')),
    ).toBe(false);
  });

  it('treats a null effective date as not set, which is how the SDK returns an empty column', () => {
    // new Date(null) is the epoch, not an invalid date, so a null effective-to must not read
    // as "retired on 1970-01-01" - that hid every current version's answers on the review page.
    expect(isVersionEffective({ al_effectivefrom: '2026-08-26', al_effectiveto: null }, asOf)).toBe(
      true,
    );
    expect(isVersionEffective({ al_effectivefrom: null, al_effectiveto: null }, asOf)).toBe(true);
  });

  it('treats an unparsable date as not set', () => {
    expect(isVersionEffective({ al_effectiveto: 'not a date' }, asOf)).toBe(true);
  });
});
