import { describe, expect, it } from 'vitest';
import { checkExtract, tokenise } from './caseUpload';
import { classify } from '../../services/commands/failures';
import { messageForFailure } from '../../services/errors';

// The same input as ImportRulesFixTests on the server, so both copies read a file alike.
const STRAY_QUOTE = 'TaskID,Client\n1,Acme 5" Ltd\n2,Beta\n3,Gamma\n';

describe('a quote inside an unquoted field', () => {
  it('is kept as text and does not swallow the rows after it', () => {
    const rows = tokenise(STRAY_QUOTE);
    expect(rows).toHaveLength(4);
    expect(rows[1]).toEqual(['1', 'Acme 5" Ltd']);
    expect(rows[3]).toEqual(['3', 'Gamma']);
  });

  it('still opens a quoted field at the start of a field', () => {
    expect(tokenise('a,"Client, A.",c\r\n')).toEqual([['a', 'Client, A.', 'c']]);
    expect(tokenise('"A ""nickname"" client"\r\n')).toEqual([['A "nickname" client']]);
  });
});

// Owner decision 2026-11-17. The same strings as ImportWholeFileFixTests on the server.
// The app holds no copy of the date rule (see caseUpload.ts), so it never refuses a file over
// a date: every row goes up and the server's answer is the one the user sees.
const DATES = ['18/08/2026 13:54', '06/08/2026 14:49', '6/8/2026 09:05:30', '13:54', '10:00 AM', 'soon'];

describe('a date column, read the same way on both sides', () => {
  it.each(DATES)('leaves "%s" for the server to accept or report by row', (value) => {
    expect(checkExtract(`TaskID,StartDate\n1,${value}\n`)).toBeNull();
  });
});

const SENTENCE =
  "Row 3 (TaskID T2) could not be saved, so nothing from this file was imported: " +
  "The adviser's email 'a@x' is not allowed. Fix that row and upload the file again.";

describe('a file the server refuses as a whole', () => {
  it.each(['VALIDATION', 'PRECONDITION'])('shows the %s sentence whole', (prefix) => {
    const body = JSON.stringify({ error: { code: '0x80040265', message: `${prefix}: ${SENTENCE}` } });
    expect(messageForFailure(classify(body))).toBe(SENTENCE);
    expect(messageForFailure(classify(new Error(`${prefix}: ${SENTENCE}`)))).toBe(SENTENCE);
  });
});

describe('a file the browser could not read', () => {
  it("shows the reader's own sentence", async () => {
    const { readFailureMessage } = await import('./caseUpload');
    expect(readFailureMessage(new Error('The file is not a valid .xlsx workbook.'))).toBe('The file is not a valid .xlsx workbook.');
  });

  it("never shows the browser's own text", async () => {
    const { readFailureMessage } = await import('./caseUpload');
    const locked = Object.assign(new Error('The requested file could not be read, typically due to permission problems'), { name: 'NotReadableError' });
    expect(readFailureMessage(locked)).toMatch(/^The file could not be read\. It may be open in another program/);
    expect(readFailureMessage(new TypeError('Failed to fetch'))).toMatch(/^The file could not be read\./);
    expect(readFailureMessage('x')).toMatch(/^The file could not be read\./);
  });
});

describe('the row limit', () => {
  it('refuses a file over the server limit before uploading it, in the server words', async () => {
    const { checkExtract, MAX_IMPORT_ROWS } = await import('./caseUpload');
    const rows = Array.from({ length: MAX_IMPORT_ROWS + 1 }, (_, i) => `T${i},A`).join('\n');
    expect(checkExtract(`TaskID,Client\n${rows}`)).toBe(
      'This file has 1001 rows. Import at most 1000 at a time, so the import completes rather than timing out part-written.',
    );
    expect(checkExtract(`TaskID,Client\n${rows.split('\n').slice(1).join('\n')}`)).toBeNull();
  });
});

describe('the row limit matches the server', () => {
  it('equals ImportRules.MaxRows', async () => {
    // Imported as text, as the other source-scanning tests do.
    const sources = import.meta.glob('../../../../plugins/OutcomeTesting.Plugins/ImportRules.cs', {
      query: '?raw',
      import: 'default',
      eager: true,
    }) as Record<string, string>;
    const source = Object.values(sources)[0] ?? '';
    const { MAX_IMPORT_ROWS } = await import('./caseUpload');
    expect(source).toContain(`public const int MaxRows = ${MAX_IMPORT_ROWS};`);
  });
});
