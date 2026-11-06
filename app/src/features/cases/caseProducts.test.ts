import { describe, expect, it } from 'vitest';
import { productNamesByCase } from './caseWorklistMapping';
import { caseExportRow, completedCaseRow } from './caseExport';
import type { CaseSummary } from './caseWorklistMapping';

describe('a case in a report', () => {
  it('has no products to name where nothing links them', () => {
    expect(productNamesByCase([], [], new Date()).size).toBe(0);
  });

  it('shows a Tax grade where the case has no advice outcome', () => {
    const row = completedCaseRow({ taxOutcome: 'Pass', latestOutcome: null } as unknown as CaseSummary);
    expect(row).toContain('Pass');
  });

  it('dates a case created just after UK midnight on the UK day, as the filter does', () => {
    const row = caseExportRow({ createdOn: '2026-07-31T23:30:00Z' } as unknown as CaseSummary);
    expect(row).toContain('2026-08-01');
  });
});
