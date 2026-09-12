import { describe, expect, it } from 'vitest';
import { inForce, OWNER_ROLE_LABEL } from './libraryStatus';

/**
 * Mirrors SectionRules.IsSectionEffective and ResponseRules.IsVersionEffective, which are
 * authoritative: in force from the start of effective-from until the start of effective-to.
 */
describe('inForce', () => {
  const today = new Date('2026-09-12');

  it('treats an undated row as in force', () => {
    expect(inForce(null, null, today)).toBe(true);
  });

  it('excludes a row dated out today', () => {
    expect(inForce(null, '2026-09-12', today)).toBe(false);
  });

  it('keeps a row dated out tomorrow', () => {
    expect(inForce(null, '2026-09-13', today)).toBe(true);
  });

  it('excludes a row not yet in force', () => {
    expect(inForce('2026-09-13', null, today)).toBe(false);
  });

  it('treats a row starting today as in force', () => {
    expect(inForce('2026-09-12', null, today)).toBe(true);
  });

  it('reads a timestamp as the day it falls on', () => {
    expect(inForce(null, '2026-09-12T23:59:00Z', today)).toBe(false);
  });

  it('treats undefined the same as null, because the generated models use it', () => {
    expect(inForce(undefined, undefined, today)).toBe(true);
  });
});

describe('OWNER_ROLE_LABEL', () => {
  it('names the three teams a section may be owned by', () => {
    expect(OWNER_ROLE_LABEL[120910100]).toBe('Tax');
    expect(OWNER_ROLE_LABEL[120910101]).toBe('AQS');
    expect(OWNER_ROLE_LABEL[120910105]).toBe('Both');
  });
});
