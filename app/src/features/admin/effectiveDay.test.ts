import { afterEach, describe, expect, it, vi } from 'vitest';
import { today } from './effectiveDay';

/**
 * The default date the checklist editors offer (AD-122, F31).
 *
 * It has to be the UTC day, because every command compares what it is given against
 * `DateTime.UtcNow.Date` and refuses anything earlier. A local-day default would be refused
 * as "in the past" for anybody east of UTC between midnight and their offset - they would
 * open the editor, touch nothing, save, and be told a date they never chose is invalid.
 */
describe('the day a checklist change takes effect by default', () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it('is yyyy-MM-dd, which is the shape the commands parse', () => {
    expect(today()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  });

  it('is the UTC day even when the local day is the one before', () => {
    // 00:30 UTC on the 21st. A browser at UTC-2 calls this the 20th, and sending the 20th
    // to a command that compares against UtcNow.Date is a refusal.
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-21T00:30:00Z'));

    expect(today()).toBe('2026-10-21');
  });

  it('is the UTC day even when the local day is the one after', () => {
    // 23:30 UTC on the 20th, which a browser at UTC+2 calls the 21st. Sending the 21st is
    // accepted but silently dates the change a day later than the administrator meant.
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-10-20T23:30:00Z'));

    expect(today()).toBe('2026-10-20');
  });
});
