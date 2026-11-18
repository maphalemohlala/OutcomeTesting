import { describe, expect, it } from 'vitest';
import { buildCsv } from '../../lib/tabular';
import { sheetXml } from '../../lib/xlsx';
import { TRAIL_LIGHT_HEADERS, closedWhenSnapshotted, fileRange, trailLightRow, type ExportRecord } from './trailLight';

const withCode = (code: string) => trailLightRow({ al_advisercode: code } as ExportRecord, 'quarterly')[1];

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
    const row = trailLightRow({ al_advisercode: '00123', al_paraplannercode: '123' } as ExportRecord, 'quarterly');

    const csv = buildCsv(TRAIL_LIGHT_HEADERS, [row]).split('\r\n')[1].split(',');
    expect(csv[1]).toBe('00123');
    expect(csv[3]).toBe('123');

    const xml = sheetXml({ name: 'Trail Light', headers: TRAIL_LIGHT_HEADERS, rows: [row] });
    expect(xml).toContain('<c r="B2" t="inlineStr"><is><t xml:space="preserve">00123</t></is></c>');
    expect(xml).toContain('<c r="D2"><v>123</v></c>');
  });
});

describe('which rows each file may take', () => {
  it('reads a row with nothing in the old separator column as closed, as every older batch is', () => {
    expect(closedWhenSnapshotted({} as ExportRecord)).toBe(true);
    expect(closedWhenSnapshotted({ al_separator: '' } as ExportRecord)).toBe(true);
    expect(closedWhenSnapshotted({ al_separator: 'Open' } as ExportRecord)).toBe(false);
  });

  it('cuts the daily file to the UK days since the previous batch, up to the day before this one', () => {
    // 23:30Z on 1 June is 2 June in London, so the first batch covers 1 June.
    expect(fileRange('daily', '', '', '2026-06-01T23:30:00Z')).toEqual({ from: '2026-06-01', to: '2026-06-01' });
    // Friday's batch, then Monday's: Monday's covers Friday to Sunday.
    expect(fileRange('daily', '', '', '2026-11-16T09:00:00Z', '2026-11-13T09:00:00Z')).toEqual({ from: '2026-11-13', to: '2026-11-15' });
    expect(fileRange('daily', '2026-11-01', '', '2026-11-17T09:00:00Z')).toEqual({ from: '2026-11-01', to: '' });
    expect(fileRange('quarterly', '', '', '2026-11-17T09:00:00Z')).toEqual({ from: '', to: '' });
    expect(fileRange('daily', '', '', null)).toEqual({ from: '', to: '' });
  });
});

describe('default daily files tile the calendar', () => {
  it('puts every check day in exactly one batch default, whatever time each batch was made', async () => {
    const { previousBatchGeneratedOn } = await import('./trailLight');
    const batches = [
      { id: 'mon', generatedOn: '2026-11-09T08:00:00Z' },
      { id: 'tue', generatedOn: '2026-11-10T17:45:00Z' },
      { id: 'tue2', generatedOn: '2026-11-10T18:00:00Z' },
      { id: 'fri', generatedOn: '2026-11-13T23:30:00Z' },
      { id: 'mon2', generatedOn: '2026-11-16T07:00:00Z' },
    ];
    const ranges = ['mon', 'tue', 'fri', 'mon2'].map((id) =>
      fileRange('daily', '', '', batches.find((b) => b.id === id)!.generatedOn, previousBatchGeneratedOn(batches, id)),
    );
    const days: string[] = [];
    for (const r of ranges) {
      for (let d = r.from; d <= r.to; d = new Date(Date.parse(d) + 86400000).toISOString().slice(0, 10)) days.push(d);
    }
    expect(days).toEqual(['2026-11-08', '2026-11-09', '2026-11-10', '2026-11-11', '2026-11-12', '2026-11-13', '2026-11-14', '2026-11-15']);
    // A second batch the same day covers the same days as the first.
    expect(fileRange('daily', '', '', batches[2].generatedOn, previousBatchGeneratedOn(batches, 'tue2'))).toEqual(ranges[1]);
  });
});
