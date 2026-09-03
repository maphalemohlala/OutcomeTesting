import { describe, expect, it } from 'vitest';
import { buildWorkbook, columnRef, crc32, escapeXml, safeSheetName, sheetXml } from './xlsx';
import { buildCsv } from './tabular';

const decoder = new TextDecoder();

describe('columnRef', () => {
  it('maps the first, last and wrapped columns', () => {
    expect(columnRef(0)).toBe('A');
    expect(columnRef(25)).toBe('Z');
    expect(columnRef(26)).toBe('AA');
    expect(columnRef(51)).toBe('AZ');
    expect(columnRef(52)).toBe('BA');
  });
});

describe('crc32', () => {
  it('matches the published check value for "123456789"', () => {
    expect(crc32(new TextEncoder().encode('123456789'))).toBe(0xcbf43926);
  });

  it('is zero for empty input', () => {
    expect(crc32(new Uint8Array())).toBe(0);
  });
});

describe('escapeXml', () => {
  it('escapes markup and strips control characters Excel would reject', () => {
    expect(escapeXml('Smith & Co <"x">')).toBe('Smith &amp; Co &lt;&quot;x&quot;&gt;');
    expect(escapeXml('a\u0007b')).toBe('ab');
  });
});

describe('safeSheetName', () => {
  it('removes the characters Excel forbids and truncates at 31', () => {
    expect(safeSheetName('Cases/2026:Q1', 'Sheet1')).toBe('Cases 2026 Q1');
    expect(safeSheetName('', 'Sheet1')).toBe('Sheet1');
    expect(safeSheetName('x'.repeat(40), 'Sheet1')).toHaveLength(31);
  });
});

describe('sheetXml', () => {
  it('writes numbers as values and text as inline strings', () => {
    const xml = sheetXml({ name: 'S', headers: ['Client', 'Age'], rows: [['A. Client', 4]] });
    expect(xml).toContain('<c r="A1" t="inlineStr"><is><t xml:space="preserve">Client</t></is></c>');
    expect(xml).toContain('<c r="B2"><v>4</v></c>');
  });

  it('omits empty cells rather than writing an empty inline string', () => {
    const xml = sheetXml({ name: 'S', headers: ['A', 'B'], rows: [[null, '']] });
    expect(xml).toContain('<row r="2"></row>');
  });
});

describe('buildWorkbook', () => {
  it('writes the four package parts plus one worksheet per sheet', () => {
    const bytes = buildWorkbook([
      { name: 'Cases', headers: ['Ref'], rows: [['IO-1']] },
      { name: 'Outcomes', headers: ['Grade'], rows: [['Pass']] },
    ]);
    const text = decoder.decode(bytes);

    for (const part of [
      '[Content_Types].xml',
      '_rels/.rels',
      'xl/workbook.xml',
      'xl/_rels/workbook.xml.rels',
      'xl/worksheets/sheet1.xml',
      'xl/worksheets/sheet2.xml',
    ]) {
      expect(text).toContain(part);
    }
    // Local file header, central directory and end-of-central-directory signatures.
    expect(text.startsWith('PK\u0003\u0004')).toBe(true);
    expect(text).toContain('PK\u0001\u0002');
    expect(text).toContain('PK\u0005\u0006');
  });

  it('refuses a workbook with no sheets', () => {
    expect(() => buildWorkbook([])).toThrow();
  });
});

describe('buildCsv', () => {
  it('quotes cells containing a comma, quote or newline', () => {
    expect(buildCsv(['A', 'B'], [['plain', 'has, comma']])).toBe('A,B\r\nplain,"has, comma"\r\n');
    expect(buildCsv(['A'], [['say "hi"']])).toBe('A\r\n"say ""hi"""\r\n');
  });

  it('renders null and undefined as empty cells', () => {
    expect(buildCsv(['A', 'B'], [[null, undefined]])).toBe('A,B\r\n,\r\n');
  });
});
