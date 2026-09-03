import { describe, expect, it } from 'vitest';
import { parseCaseCsv } from './caseUpload';

/**
 * The client parser is a preview of BR-002, not the rule -- `ImportRules` in the plug-in
 * decides what is actually imported (AD-003). These cases pin the two together: a value
 * the preview accepts and the command rejects would show a user "will import" and then
 * hand them a rejection, and the ambiguous ones below are exactly where that happens.
 */

const HEADER = 'IO reference,Client name,Advice date,Case type';

function parse(...rows: string[]) {
  return parseCaseCsv(`${HEADER}\r\n${rows.join('\r\n')}\r\n`);
}

describe('parseCaseCsv dates', () => {
  it('reads a slash date as day-first', () => {
    // 03/09/2026 is 3 September in an Intelligent Office extract. Month-first it becomes
    // 9 March, and nothing downstream would ever flag it.
    expect(parse('IO-1,,03/09/2026,').valid[0].record.al_advicedate).toBe('2026-09-03');
  });

  it('rejects a slash date that is not day-first', () => {
    expect(parse('IO-1,,01/13/2026,').valid).toHaveLength(0);
    expect(parse('IO-1,,01/13/2026,').invalid[0].reason).toContain('Advice date');
  });

  it('rejects a day that does not exist in that month', () => {
    expect(parse('IO-1,,31/02/2026,').valid).toHaveLength(0);
  });

  it('reads an ISO date', () => {
    expect(parse('IO-1,,2026-01-31,').valid[0].record.al_advicedate).toBe('2026-01-31');
  });

  it('reads an ISO timestamp as its date', () => {
    expect(parse('IO-1,,2026-01-31T09:30:00Z,').valid[0].record.al_advicedate).toBe('2026-01-31');
  });

  it('accepts a written-out date, which cannot be misread', () => {
    expect(parse('IO-1,,31 Jan 2026,').valid[0].record.al_advicedate).toBe('2026-01-31');
  });

  it('rejects an unreadable date', () => {
    expect(parse('IO-1,,not a date,').valid).toHaveLength(0);
  });
});

describe('parseCaseCsv rows', () => {
  it('requires only the IO reference (BR-001)', () => {
    expect(parse('IO-1,,,').valid).toHaveLength(1);
  });

  it('rejects a row with no reference', () => {
    expect(parse(',A. Client,,').invalid[0].reason).toContain('Missing IO reference');
  });

  it('rejects the second row naming a reference already in the file', () => {
    const result = parse('IO-1,,,', 'IO-1,,,');
    expect(result.valid).toHaveLength(1);
    expect(result.invalid[0].reason).toContain('Duplicate IO reference');
  });

  it('rejects a choice value that is not an option', () => {
    expect(parse('IO-1,,,Not a case type').invalid[0].reason).toContain('Case type');
  });

  it('accepts a raw option value so coded extracts import', () => {
    expect(parse('IO-1,,,120910511').valid[0].record.al_casetype).toBe(120910511);
  });

  it('skips the template guide row', () => {
    expect(parse('IO-000123,A. Client,,', 'IO-1,,,').valid).toHaveLength(1);
  });

  it('reports a file with no IO reference column as fatal', () => {
    expect(parseCaseCsv('Client name\r\nA. Client\r\n').fatal).toContain('IO reference');
  });
});
