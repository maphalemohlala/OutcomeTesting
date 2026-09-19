import { describe, expect, it } from 'vitest';
import { ADVICE_DATE_LABEL, adviceDateRefusal, ukToday } from './caseHeaderDates';

/**
 * The client-side mirror of CaseHeaderRules. The same cases the C# tests cover, so a change
 * to one that is not made to the other shows up as a disagreement between two suites rather
 * than as a rule that holds on one surface only.
 */
describe('ukToday', () => {
  it('reads a British Summer Time evening as the next day', () => {
    // 30 June 2026 at 23:30 UTC is 00:30 on 1 July in London. A browser reading UTC - or
    // sitting in another zone - would call it 30 June and refuse a checker's own today.
    expect(ukToday(new Date('2026-06-30T23:30:00Z'))).toBe('2026-07-01');
  });

  it('leaves a winter evening on its own day', () => {
    // January is GMT, so there is no offset to apply.
    expect(ukToday(new Date('2026-01-15T23:30:00Z'))).toBe('2026-01-15');
  });

  it('formats as yyyy-MM-dd, the order a date input takes and the order that sorts', () => {
    expect(ukToday(new Date('2026-03-05T12:00:00Z'))).toBe('2026-03-05');
  });
});

describe('adviceDateRefusal', () => {
  const today = '2026-01-15';

  it('accepts a date in the past', () => {
    expect(adviceDateRefusal('2026-01-01', today)).toBeNull();
  });

  it('accepts today', () => {
    // A meeting held this morning is checked this afternoon often enough that refusing
    // today would be refusing the ordinary case.
    expect(adviceDateRefusal(today, today)).toBeNull();
  });

  it('refuses tomorrow, naming the field as the business names it', () => {
    expect(adviceDateRefusal('2026-01-16', today)).toBe(
      'Date of meeting - Client contact cannot be in the future.',
    );
  });

  it('accepts an empty value, because clearing the field is a legitimate edit', () => {
    expect(adviceDateRefusal('', today)).toBeNull();
    expect(adviceDateRefusal(null, today)).toBeNull();
    expect(adviceDateRefusal(undefined, today)).toBeNull();
  });

  it('accepts a meeting on the due date itself', () => {
    // The deadline is a day, not an instant.
    expect(adviceDateRefusal('2026-01-10', today, '2026-01-10')).toBeNull();
  });

  it('refuses a meeting after the due date and says when that was', () => {
    // Worded to the character as CaseHeaderRules words it, so the two rules are one message.
    expect(adviceDateRefusal('2026-01-12', today, '2026-01-10')).toBe(
      'Date of meeting - Client contact cannot be later than the due date (10 Jan 2026).',
    );
  });

  it('reports the future before the due date when both are wrong', () => {
    expect(adviceDateRefusal('2099-01-01', today, '2026-01-10')).toBe(
      'Date of meeting - Client contact cannot be in the future.',
    );
  });

  it('accepts any past meeting when the case carries no due date', () => {
    expect(adviceDateRefusal('2026-01-10', today, null)).toBeNull();
    expect(adviceDateRefusal('2026-01-10', today, '')).toBeNull();
  });

  it('reads a due date that arrives as a timestamp', () => {
    // al_duedate is stamped 72 hours after the upload, so the app may hold it with a time.
    expect(adviceDateRefusal('2026-01-10', today, '2026-01-10T09:00:00Z')).toBeNull();
    expect(adviceDateRefusal('2026-01-11', today, '2026-01-10T09:00:00Z')).toBe(
      'Date of meeting - Client contact cannot be later than the due date (10 Jan 2026).',
    );
  });

  it('words the refusal exactly as the label reads', () => {
    expect(adviceDateRefusal('2099-01-01', today)).toContain(ADVICE_DATE_LABEL);
  });
});
