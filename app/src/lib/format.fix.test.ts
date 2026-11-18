import { afterEach, describe, expect, it, vi } from 'vitest';
import { dateTime } from './format';
import { ukDayOf } from '../features/cases/caseHeaderDates';

afterEach(() => {
  vi.unstubAllEnvs();
});

describe('dateTime', () => {
  it('shows a time on the UK day the filters put it on, whatever the browser zone', () => {
    vi.stubEnv('TZ', 'Africa/Johannesburg');
    const generated = '2026-11-16T22:30:00Z';
    expect(ukDayOf(generated)).toBe('2026-11-16');
    expect(dateTime(generated)).toBe('16 Nov 2026, 22:30');
  });

  it('follows British Summer Time', () => {
    vi.stubEnv('TZ', 'America/New_York');
    expect(dateTime('2026-07-01T23:30:00Z')).toBe('02 Jul 2026, 00:30');
  });

  it('reads an absent or broken value as a dash', () => {
    expect(dateTime(null)).toBe('—');
    expect(dateTime(undefined)).toBe('—');
    expect(dateTime('not a date')).toBe('—');
  });
});
