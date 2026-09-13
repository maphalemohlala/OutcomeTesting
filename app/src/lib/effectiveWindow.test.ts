import { describe, expect, it } from 'vitest';
import { dayOf, inForceOn } from './effectiveWindow';

/**
 * Mirrors SectionRules.IsSectionEffective and ResponseRules.IsVersionEffective, which are
 * authoritative: in force from the start of effective-from until the start of effective-to.
 *
 * Tested here, on the rule itself, rather than through the `libraryStatus.inForce` alias
 * the Question library used to import - a one-line delegate that existed only to rename
 * this function (audit of 2026-09-13).
 */
describe('inForceOn', () => {
  const today = new Date('2026-09-12');

  it('treats an undated row as in force', () => {
    expect(inForceOn(null, null, today)).toBe(true);
  });

  it('excludes a row dated out today', () => {
    expect(inForceOn(null, '2026-09-12', today)).toBe(false);
  });

  it('keeps a row dated out tomorrow', () => {
    expect(inForceOn(null, '2026-09-13', today)).toBe(true);
  });

  it('excludes a row not yet in force', () => {
    expect(inForceOn('2026-09-13', null, today)).toBe(false);
  });

  it('treats a row starting today as in force', () => {
    expect(inForceOn('2026-09-12', null, today)).toBe(true);
  });

  it('reads a timestamp as the day it falls on', () => {
    expect(inForceOn(null, '2026-09-12T23:59:00Z', today)).toBe(false);
  });

  it('treats undefined the same as null, because the generated models use it', () => {
    expect(inForceOn(undefined, undefined, today)).toBe(true);
  });
});

describe('dayOf', () => {
  it('is null for an unset or unreadable value', () => {
    expect(dayOf(null)).toBeNull();
    expect(dayOf(undefined)).toBeNull();
    expect(dayOf('not a date')).toBeNull();
  });

  it('is the UTC day a timestamp falls on', () => {
    expect(dayOf('2026-09-12T23:59:00Z')).toBe(Date.UTC(2026, 8, 12));
  });
});
