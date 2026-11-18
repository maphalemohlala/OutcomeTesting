import { describe, expect, it } from 'vitest';
import { dailyFileDay, dailyFileMayBeShort, trailLightFilename } from './trailLight';

describe('a daily file is named for the day it covers', () => {
  it('uses the chosen day, not the day of the download', () => {
    const tomorrow = new Date('2026-11-18T09:00:00Z');
    expect(trailLightFilename('csv', tomorrow, dailyFileDay('daily', { from: '2026-11-17', to: '2026-11-17' })))
      .toBe('DFALIN1_outcometesting_2026_11_17.csv');
  });

  it('keeps the download day for the quarterly file and for a range of days', () => {
    const now = new Date('2026-11-18T09:00:00Z');
    expect(trailLightFilename('csv', now, dailyFileDay('quarterly', { from: '2026-11-17', to: '2026-11-17' })))
      .toBe('DFALIN1_outcometesting_2026_11_18.csv');
    expect(trailLightFilename('csv', now, dailyFileDay('daily', { from: '2026-11-10', to: '2026-11-17' })))
      .toBe('DFALIN1_outcometesting_2026_11_18.csv');
  });
});

describe('a daily file that may miss later checks says so', () => {
  const generatedOn = '2026-11-17T14:02:00Z';

  it('warns when the file covers the day the batch was generated', () => {
    expect(dailyFileMayBeShort('daily', { from: '2026-11-17', to: '2026-11-17' }, generatedOn)).toBe(true);
  });

  it('does not warn for a day that was over when the batch was generated', () => {
    expect(dailyFileMayBeShort('daily', { from: '2026-11-16', to: '2026-11-16' }, generatedOn)).toBe(false);
  });

  it('does not warn for the quarterly file', () => {
    expect(dailyFileMayBeShort('quarterly', { from: '2026-11-17', to: '2026-11-17' }, generatedOn)).toBe(false);
  });
});
