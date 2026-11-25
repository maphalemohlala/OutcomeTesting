import { describe, expect, it } from 'vitest';
import { dailyFileDay, dayBefore, trailLightFilename } from './trailLight';

describe('a daily file is named for the day it covers', () => {
  it('uses the chosen day, not the day of the download', () => {
    const tomorrow = new Date('2026-11-18T09:00:00Z');
    expect(trailLightFilename('csv', tomorrow, dailyFileDay('Daily', '2026-11-17', '2026-11-17')))
      .toBe('DFALIN1_outcometesting_2026_11_17.csv');
  });

  it('keeps the download day for the quarterly file and for a range of days', () => {
    const now = new Date('2026-11-18T09:00:00Z');
    expect(trailLightFilename('csv', now, dailyFileDay('Quarterly', '2026-11-17', '2026-11-17')))
      .toBe('DFALIN1_outcometesting_2026_11_18.csv');
    expect(trailLightFilename('csv', now, dailyFileDay('Daily', '2026-11-10', '2026-11-17')))
      .toBe('DFALIN1_outcometesting_2026_11_18.csv');
    expect(trailLightFilename('csv', now, dailyFileDay('Daily', '', '')))
      .toBe('DFALIN1_outcometesting_2026_11_18.csv');
  });
});

describe('the daily file starts on yesterday', () => {
  it('steps back one calendar day across month and year ends', () => {
    expect(dayBefore('2026-11-25')).toBe('2026-11-24');
    expect(dayBefore('2026-03-01')).toBe('2026-02-28');
    expect(dayBefore('2027-01-01')).toBe('2026-12-31');
  });
});
