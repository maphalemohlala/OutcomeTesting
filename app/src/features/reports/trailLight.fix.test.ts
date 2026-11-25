import { describe, expect, it } from 'vitest';
import { buildCsv } from '../../lib/tabular';
import { sheetXml } from '../../lib/xlsx';
import { TRAIL_LIGHT_HEADERS, trailLightRow, type ExportRecord } from './trailLight';

const withCode = (code: string) => trailLightRow({ al_advisercode: code } as ExportRecord)[1];

describe('a staff code is a number only when the number reads back as the same text', () => {
  it('writes a plain code as a number', () => {
    expect(withCode('123')).toBe(123);
  });

  it('keeps everything else as the text given', () => {
    for (const code of ['00123', '1.50', '+44', '1 2', '12345678901234567890', '9007199254740993', '-0', '1e3', 'Infinity', 'NaN']) {
      expect(withCode(code)).toBe(code);
    }
  });

  it('keeps the leading zeros in the CSV and the XLSX alike', () => {
    const row = trailLightRow({ al_advisercode: '00123', al_paraplannercode: '123' } as ExportRecord);

    const csv = buildCsv(TRAIL_LIGHT_HEADERS, [row]).split('\r\n')[1].split(',');
    expect(csv[1]).toBe('00123');
    expect(csv[3]).toBe('123');

    const xml = sheetXml({ name: 'Trail Light', headers: TRAIL_LIGHT_HEADERS, rows: [row] });
    expect(xml).toContain('<c r="B2" t="inlineStr"><is><t xml:space="preserve">00123</t></is></c>');
    expect(xml).toContain('<c r="D2"><v>123</v></c>');
  });
});
