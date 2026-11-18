import { describe, expect, it } from 'vitest';
import { reportDate } from './CompletedCaseReport';

describe('the completed-case report date', () => {
  it('reads intake on the UK day, not the UTC one', () => {
    // 23:30 UTC on 30 June is 00:30 on 1 July in London (BST).
    expect(reportDate({ checkDate: null, createdOn: '2026-06-30T23:30:00Z' })).toBe('2026-07-01');
  });

  it('keeps the check date as the business date where there is one', () => {
    expect(reportDate({ checkDate: '2026-06-30', createdOn: '2026-06-01T10:00:00Z' })).toBe('2026-06-30');
  });

  it('is blank when neither is held', () => {
    expect(reportDate({ checkDate: null, createdOn: null })).toBe('');
  });
});
