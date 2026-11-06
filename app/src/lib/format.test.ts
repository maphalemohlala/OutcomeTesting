import { describe, expect, it } from 'vitest';
import { date } from './format';

describe('date', () => {
  it('writes the short month the portal writes, not the browser locale\'s "Sept"', () => {
    expect(date('2026-09-07')).toBe('07 Sep 2026');
  });

  it('is the UK day, whatever zone the browser is in', () => {
    expect(date('2026-07-31T23:30:00Z')).toBe('01 Aug 2026');
  });

  it('is blank for nothing, or for what is not a date', () => {
    expect(date(null)).toBeNull();
    expect(date('not a date')).toBeNull();
  });
});
