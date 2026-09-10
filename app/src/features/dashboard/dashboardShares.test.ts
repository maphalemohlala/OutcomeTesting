import { describe, expect, it } from 'vitest';
import { proportion } from './dashboardShares';

describe('proportion', () => {
  it('is the count as a percentage of the total, to one decimal place', () => {
    expect(proportion(4, 18)).toBe(22.2);
    expect(proportion(9, 14)).toBe(64.3);
  });

  it('fills the bar when the count is the whole', () => {
    expect(proportion(18, 18)).toBe(100);
  });

  it('never exceeds the bar', () => {
    expect(proportion(20, 18)).toBe(100);
  });

  it('is empty for a zero count or an empty whole', () => {
    expect(proportion(0, 18)).toBe(0);
    expect(proportion(5, 0)).toBe(0);
    expect(proportion(0, 0)).toBe(0);
  });

  it('treats a nonsensical negative or NaN input as empty', () => {
    expect(proportion(-1, 18)).toBe(0);
    expect(proportion(3, Number.NaN)).toBe(0);
  });
});
