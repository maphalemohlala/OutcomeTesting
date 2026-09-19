import { describe, expect, it } from 'vitest';
import { CASE_EXPORT_HEADERS, caseExportRow } from './caseExport';
import { UPLOADED_BY_LABEL } from '../../types/domain';
import type { CaseSummary } from './caseWorklistMapping';

/**
 * The worklist CSV, which is the one place the app's column names leave the app. Two of them
 * changed on 2026-09-19 and both are pinned here, because a header a person has built a
 * spreadsheet around is a contract whether or not anyone called it one.
 */

function row(overrides: Partial<CaseSummary> = {}): CaseSummary {
  return {
    id: 'c1',
    caseReference: 'IO-300001',
    route: 'Tax then AQS',
    status: 'Queued',
    owner: 'Ida Uploader',
    priority: null,
    createdOn: null,
    ageInDays: 0,
    latestOutcome: null,
    taxOutcome: null,
    initialOutcome: null,
    finalOutcome: null,
    finalisedOn: null,
    nextAction: '',
    client: null,
    adviser: null,
    adviserCode: null,
    paraplanner: null,
    paraplannerCode: null,
    taxChecker: 'Tom Tax',
    aqsChecker: 'Ada Aqs',
    caseType: null,
    productSolutionType: null,
    products: null,
    adviceDate: null,
    checkDate: null,
    preOrPostCheck: null,
    dueDate: null,
    ...overrides,
  };
}

describe('CASE_EXPORT_HEADERS', () => {
  it('calls the case owner column "Uploaded by" (item 4, 2026-09-19)', () => {
    // A label change and nothing else: the column is the Dataverse record owner, and it reads
    // as the uploader because nothing in the solution ever reassigns it.
    expect(CASE_EXPORT_HEADERS).toContain(UPLOADED_BY_LABEL);
    expect(CASE_EXPORT_HEADERS).not.toContain('Owner');
  });

  it('carries one checker column per discipline (item 2, 2026-09-19)', () => {
    expect(CASE_EXPORT_HEADERS).toContain('Tax Checker');
    expect(CASE_EXPORT_HEADERS).toContain('AQS Checker');
    expect(CASE_EXPORT_HEADERS).not.toContain('Checker name');
  });

  it('gives every header a value, and every value a header', () => {
    // The two changes above each moved a column. A row and a header that disagree on width
    // silently shifts every column after the break.
    expect(caseExportRow(row())).toHaveLength(CASE_EXPORT_HEADERS.length);
  });
});

describe('caseExportRow', () => {
  it('writes the two checkers in the order the header names them', () => {
    const cells = caseExportRow(row());
    const tax = CASE_EXPORT_HEADERS.indexOf('Tax Checker');
    const aqs = CASE_EXPORT_HEADERS.indexOf('AQS Checker');

    expect(cells[tax]).toBe('Tom Tax');
    expect(cells[aqs]).toBe('Ada Aqs');
  });

  it('writes the uploader under its own header', () => {
    const cells = caseExportRow(row());

    expect(cells[CASE_EXPORT_HEADERS.indexOf(UPLOADED_BY_LABEL)]).toBe('Ida Uploader');
  });

  it('leaves an unallocated checker blank rather than inventing an empty state', () => {
    // The empty states belong to the screen, where a reader can see which case they are
    // looking at. A spreadsheet cell reading "Not yet allocated" would be sorted and filtered
    // as though it were a person's name.
    const cells = caseExportRow(row({ taxChecker: null, aqsChecker: null }));

    expect(cells[CASE_EXPORT_HEADERS.indexOf('Tax Checker')]).toBe('');
    expect(cells[CASE_EXPORT_HEADERS.indexOf('AQS Checker')]).toBe('');
  });
});
