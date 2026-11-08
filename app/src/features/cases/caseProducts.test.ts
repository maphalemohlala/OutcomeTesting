import { describe, expect, it } from 'vitest';
import { productNamesByCase, staffCodesByEmail, submissionsByCase } from './caseWorklistMapping';
import { COMPLETED_CASE_HEADERS, caseExportRow, completedCaseRow } from './caseExport';
import type { CaseSummary } from './caseWorklistMapping';

describe('a case in a report', () => {
  it('has no products to name where nothing links them', () => {
    expect(productNamesByCase([], [], new Date()).size).toBe(0);
  });

  it('shows a Tax grade where the case has no advice outcome', () => {
    const row = completedCaseRow({ taxOutcome: 'Pass', latestOutcome: null } as unknown as CaseSummary);
    expect(row).toContain('Pass');
  });

  it('exports checkers, the Tax grade and how long each stage took', () => {
    const row = completedCaseRow({
      taxChecker: 'Tia Tax',
      aqsChecker: 'Abe Aqs',
      taxOutcome: 'Pass with issues',
      createdOn: '2026-10-01T09:00:00Z',
      taxSubmittedOn: '2026-10-04T09:00:00Z',
      aqsSubmittedOn: '2026-10-09T09:00:00Z',
      finalisedOn: '2026-10-11T09:00:00Z',
    } as unknown as CaseSummary);
    const cell = (header: string) => row[COMPLETED_CASE_HEADERS.indexOf(header)];

    expect(row).toHaveLength(COMPLETED_CASE_HEADERS.length);
    expect(cell('Tax Checker')).toBe('Tia Tax');
    expect(cell('AQS Checker')).toBe('Abe Aqs');
    expect(cell('Tax outcome')).toBe('Pass with issues');
    expect(cell('Received on')).toBe('2026-10-01');
    expect(cell('Days in Tax')).toBe(3);
    expect(cell('Days in AQS')).toBe(5);
    expect(cell('Overall age (days)')).toBe(10);
  });

  it('times an AQS-only case from receipt and leaves the Tax stage blank', () => {
    const row = completedCaseRow({
      createdOn: '2026-10-01T09:00:00Z',
      taxSubmittedOn: null,
      aqsSubmittedOn: '2026-10-03T09:00:00Z',
    } as unknown as CaseSummary);

    expect(row[COMPLETED_CASE_HEADERS.indexOf('Days in Tax')]).toBe('');
    expect(row[COMPLETED_CASE_HEADERS.indexOf('Days in AQS')]).toBe(2);
  });

  it('takes the later submission of a check that was submitted twice', () => {
    const byCase = submissionsByCase([
      { _al_outcomecaseid_value: 'c1', al_reviewtype: 120910200, al_submittedon: '2026-10-01T00:00:00Z' },
      { _al_outcomecaseid_value: 'c1', al_reviewtype: 120910200, al_submittedon: '2026-10-05T00:00:00Z' },
      { _al_outcomecaseid_value: 'c1', al_reviewtype: 120910201, al_submittedon: '2026-10-07T00:00:00Z' },
    ]);
    expect(byCase.get('c1')).toEqual({ tax: '2026-10-05T00:00:00Z', aqs: '2026-10-07T00:00:00Z' });
  });

  it('finds a staff code by email, ignoring case, and never on a shared address', () => {
    const codes = staffCodesByEmail([
      { emailaddress1: 'Ann@Example.com', al_staffcode: ' A1 ', statecode: 0 },
      { emailaddress1: 'gone@example.com', al_staffcode: 'G1', statecode: 1 },
      { emailaddress1: 'twin@example.com', al_staffcode: 'T1', statecode: 0 },
      { emailaddress1: 'twin@example.com', al_staffcode: 'T2', statecode: 0 },
    ]);
    expect([...codes]).toEqual([['ann@example.com', 'A1']]);
  });

  it('dates a case created just after UK midnight on the UK day, as the filter does', () => {
    const row = caseExportRow({ createdOn: '2026-07-31T23:30:00Z' } as unknown as CaseSummary);
    expect(row).toContain('2026-08-01');
  });
});
