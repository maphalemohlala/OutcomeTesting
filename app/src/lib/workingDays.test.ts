import { describe, expect, it } from 'vitest';
import {
  REMEDIATION_THRESHOLD_WORKING_DAYS,
  addWorkingDays,
  hasBreachedRemediationThreshold,
  isWorkingDay,
  workingDayAge,
  workingDaysBetween,
} from './workingDays';

/** A UK calendar date at midday UTC, which is unambiguous under both GMT and BST. */
const at = (iso: string): Date => new Date(`${iso}T12:00:00Z`);

describe('isWorkingDay', () => {
  it('counts Monday to Friday', () => {
    // 2026-08-24 is a Monday.
    expect(isWorkingDay(at('2026-08-24'))).toBe(true);
    expect(isWorkingDay(at('2026-08-28'))).toBe(true);
  });

  it('excludes Saturday and Sunday', () => {
    expect(isWorkingDay(at('2026-08-29'))).toBe(false);
    expect(isWorkingDay(at('2026-08-30'))).toBe(false);
  });

  it('counts an England bank holiday as a working day', () => {
    // OD-018 settled the holiday set as England's but directed it be applied manually
    // rather than encoded. 2026-08-31 is the late summer bank holiday, and it counts here.
    // This test exists so the limitation is visible rather than discovered in a breach report.
    expect(isWorkingDay(at('2026-08-31'))).toBe(true);
  });
});

describe('workingDaysBetween', () => {
  it('treats the day remediation was raised as day 1', () => {
    const monday = at('2026-08-24');
    expect(workingDaysBetween(monday, monday)).toBe(1);
  });

  it('counts consecutive weekdays inclusively', () => {
    expect(workingDaysBetween(at('2026-08-24'), at('2026-08-28'))).toBe(5);
  });

  it('does not count the weekend', () => {
    // Friday to the following Monday is two working days, not four.
    expect(workingDaysBetween(at('2026-08-28'), at('2026-08-31'))).toBe(2);
  });

  it('reaches ten working days two calendar weeks later', () => {
    // Monday to the Friday of the following week: the BR-010 threshold.
    expect(workingDaysBetween(at('2026-08-24'), at('2026-09-04'))).toBe(10);
  });

  it('returns 0 when the end is before the start', () => {
    expect(workingDaysBetween(at('2026-08-28'), at('2026-08-24'))).toBe(0);
  });

  it('counts a weekend-only span as zero working days', () => {
    expect(workingDaysBetween(at('2026-08-29'), at('2026-08-30'))).toBe(0);
  });
});

describe('UK time zone handling', () => {
  it('assigns a late BST evening to the UK day, not the UTC day', () => {
    // 22:30Z on 30 June is 23:30 BST the same day; 23:30Z is 00:30 on 1 July UK time.
    // Reading the UTC date would put the second one a day earlier and age it short.
    const lateEvening = new Date('2026-06-30T23:30:00Z');
    const nextDayUk = new Date('2026-07-01T09:00:00Z');
    expect(workingDaysBetween(lateEvening, nextDayUk)).toBe(1);
  });

  it('is unaffected by GMT, where UTC and UK time agree', () => {
    const winterEvening = new Date('2026-01-14T23:30:00Z');
    const nextMorning = new Date('2026-01-15T09:00:00Z');
    expect(workingDaysBetween(winterEvening, nextMorning)).toBe(2);
  });
});

describe('workingDayAge', () => {
  it('is 0 for a missing or unparseable value', () => {
    expect(workingDayAge(undefined)).toBe(0);
    expect(workingDayAge('not a date')).toBe(0);
  });

  it('ages from the raised date to now', () => {
    expect(workingDayAge('2026-08-24T09:00:00Z', at('2026-08-28'))).toBe(5);
  });
});

describe('addWorkingDays', () => {
  it('puts ten working days from a Monday on the Friday of the following week', () => {
    const due = addWorkingDays(at('2026-08-24'), REMEDIATION_THRESHOLD_WORKING_DAYS);
    expect(due.toISOString().slice(0, 10)).toBe('2026-09-04');
  });

  it('skips the weekend', () => {
    const due = addWorkingDays(at('2026-08-28'), 2);
    expect(due.toISOString().slice(0, 10)).toBe('2026-08-31');
  });

  it('lands on a working day when started on a weekend', () => {
    const due = addWorkingDays(at('2026-08-29'), 1);
    expect(isWorkingDay(due)).toBe(true);
    expect(due.toISOString().slice(0, 10)).toBe('2026-08-31');
  });
});

describe('hasBreachedRemediationThreshold', () => {
  it('does not breach on the tenth working day', () => {
    expect(hasBreachedRemediationThreshold('2026-08-24T09:00:00Z', at('2026-09-04'))).toBe(false);
  });

  it('breaches on the eleventh', () => {
    expect(hasBreachedRemediationThreshold('2026-08-24T09:00:00Z', at('2026-09-07'))).toBe(true);
  });

  it('does not breach on a missing date', () => {
    expect(hasBreachedRemediationThreshold(undefined)).toBe(false);
  });
});
