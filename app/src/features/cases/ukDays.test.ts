import { describe, expect, it } from 'vitest';
import { isPastDue, ukDayOf } from './caseHeaderDates';
import { withinRange } from './worklistFilters';

describe('the UK day of a timestamp', () => {
  it('moves to the next day in the hour after UK midnight in summer', () => {
    expect(ukDayOf('2026-07-31T23:30:00Z')).toBe('2026-08-01');
  });

  it('matches the UTC day in winter', () => {
    expect(ukDayOf('2026-01-31T23:30:00Z')).toBe('2026-01-31');
  });

  it('is null for a value that is not a date', () => {
    expect(ukDayOf('not a date')).toBeNull();
  });
});

describe('date filters', () => {
  it('count a case created at 00:30 on 1 August UK time as August', () => {
    expect(withinRange('2026-07-31T23:30:00Z', '2026-08-01', '2026-08-31')).toBe(true);
    expect(withinRange('2026-07-31T23:30:00Z', '2026-07-01', '2026-07-31')).toBe(false);
  });

  it('leave out an unreadable date once a range is set', () => {
    expect(withinRange('not a date', '2026-08-01', '')).toBe(false);
  });
});

describe('a due date', () => {
  it('is not overdue on the day it falls due', () => {
    expect(isPastDue('2026-11-10', '2026-11-10')).toBe(false);
  });

  it('is overdue from the next day', () => {
    expect(isPastDue('2026-11-10', '2026-11-11')).toBe(true);
  });

  it('reads the same whether the column arrives as a date or a timestamp', () => {
    expect(isPastDue('2026-11-10T00:00:00Z', '2026-11-10')).toBe(false);
  });

  it('is never overdue when there is none', () => {
    expect(isPastDue(undefined, '2026-11-11')).toBe(false);
    expect(isPastDue(null, '2026-11-11')).toBe(false);
  });
});
