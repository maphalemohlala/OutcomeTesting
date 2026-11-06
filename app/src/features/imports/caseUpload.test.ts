import { describe, expect, it } from 'vitest';
// The fixture is loaded through Vite rather than node:fs, so the app project keeps its
// browser-only type scope.
import fixture from '../../../../data/io-task-extract-sample.csv?raw';
import { buildValidationReportCsv, checkExtract, decodeCsv } from './caseUpload';

describe('the look at a file before it is sent', () => {
  it('lets the sample extract through', () => {
    expect(checkExtract(fixture)).toBeNull();
  });

  it('names a file with no TaskID column', () => {
    expect(checkExtract('Client,Adviser\nA,B\n')).toContain('"TaskID"');
  });

  it('finds the column whatever its case or spacing', () => {
    expect(checkExtract(' taskid ,Client\n1,A\n')).toBeNull();
  });

  it('says when a file has a header and nothing under it', () => {
    expect(checkExtract('TaskID,Client\n,\n')).toBe('The file has no case rows to import.');
  });

  it('says when a file is empty', () => {
    expect(checkExtract('')).toBe('The file is empty.');
  });

  it('leaves row rules to the server, so a bad row still goes up to be reported', () => {
    expect(checkExtract('TaskID,CompletedDate\n1,not a date\n')).toBeNull();
  });
});

describe('reading a CSV file', () => {
  const bytes = (...values: number[]) => new Uint8Array(values).buffer;

  it('reads UTF-8, and drops the byte order mark', () => {
    expect(decodeCsv(bytes(0xef, 0xbb, 0xbf, 0x52, 0xc3, 0xa9))).toBe('Ré');
  });

  it("falls back to Excel's plain CSV encoding rather than garbling a name", () => {
    expect(decodeCsv(bytes(0x52, 0x65, 0x6e, 0xe9, 0x65))).toBe('Renée');
  });
});

describe('the validation report', () => {
  it('cannot carry a formula from the file into Excel', () => {
    const csv = buildValidationReportCsv([
      { rowNumber: 2, caseReference: null, status: 'Failed', reason: 'Missing TaskID.', raw: '=1+1' },
    ]);

    expect(csv).toBe(`Row,TaskID,Status,Reason,Original row\r\n2,,Failed,Missing TaskID.,'=1+1\r\n`);
  });
});
