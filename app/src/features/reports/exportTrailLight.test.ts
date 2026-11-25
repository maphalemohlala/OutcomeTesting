import { beforeEach, describe, expect, it, vi } from 'vitest';

const generateExport = vi.fn();
const loadExportRecords = vi.fn();
const downloadWorkbook = vi.fn();
const downloadCsv = vi.fn();

vi.mock('../../services/commands/exports', () => ({ generateExport }));
vi.mock('./useExports', () => ({ loadExportRecords }));
vi.mock('../../lib/tabular', () => ({ downloadWorkbook, downloadCsv }));
vi.mock('../../services/errors', () => ({ messageForFailure: () => 'refused' }));

const { exportTrailLight } = await import('./exportTrailLight');

const choice = { file: 'Daily' as const, from: '2026-11-24', to: '2026-11-24', format: 'xlsx' as const, key: 'k-1' };
const ok = (rows: number) => ({ ok: true, data: { BatchId: 'k-1', RowCount: String(rows), Status: 'Generated', AuditEventId: 'a', Conflict: false } });

beforeEach(() => {
  vi.clearAllMocks();
});

describe('one Export produces and downloads the file', () => {
  it('sends the choice in one call, the key doubling as the new batch id', async () => {
    generateExport.mockResolvedValue(ok(0));

    await exportTrailLight(choice);

    expect(generateExport).toHaveBeenCalledOnce();
    expect(generateExport).toHaveBeenCalledWith({
      batchId: 'k-1', idempotencyKey: 'k-1', fileKind: 'Daily', checkedFrom: '2026-11-24', checkedTo: '2026-11-24',
    });
  });

  it('downloads the batch rows under the day the daily file covers', async () => {
    generateExport.mockResolvedValue(ok(2));
    loadExportRecords.mockResolvedValue([
      { al_advisername: 'Jane Adviser', al_advicequalitygrade: 'Potential harm' },
      { al_advisername: 'Sam Adviser', al_filequalitygrade: 'Fail' },
    ]);

    const outcome = await exportTrailLight(choice);

    expect(loadExportRecords).toHaveBeenCalledWith('k-1');
    expect(downloadWorkbook).toHaveBeenCalledOnce();
    const [name, sheets] = downloadWorkbook.mock.calls[0];
    expect(name).toBe('DFALIN1_outcometesting_2026_11_24.xlsx');
    expect(sheets[0].rows).toHaveLength(2);
    expect(sheets[0].rows[0][0]).toBe('Jane Adviser');
    expect(sheets[0].rows[0][14]).toBe('Potential harm');
    expect(outcome).toEqual({
      tone: 'ok',
      message: 'Downloaded DFALIN1_outcometesting_2026_11_24.xlsx: 2 case(s), checked on 24 Nov 2026.',
      produced: true,
      settled: true,
    });
  });

  it('writes a CSV when asked', async () => {
    generateExport.mockResolvedValue(ok(1));
    loadExportRecords.mockResolvedValue([{ al_advisername: 'Jane Adviser' }]);

    await exportTrailLight({ ...choice, format: 'csv' });

    expect(downloadWorkbook).not.toHaveBeenCalled();
    expect(downloadCsv.mock.calls[0][0]).toBe('DFALIN1_outcometesting_2026_11_24.csv');
  });

  it('downloads nothing, and keeps the intent, when fewer rows come back than were recorded', async () => {
    generateExport.mockResolvedValue(ok(3));
    loadExportRecords.mockResolvedValue([{}, {}]);

    const outcome = await exportTrailLight(choice);

    expect(downloadWorkbook).not.toHaveBeenCalled();
    expect(outcome.settled).toBe(false);
    expect(outcome.message).toBe('The file holds 3 case(s) but 2 could be read, so it was not downloaded. Select Export again to retry.');
  });

  it('says so when the rows cannot be read at all', async () => {
    generateExport.mockResolvedValue(ok(3));
    loadExportRecords.mockResolvedValue(null);

    const outcome = await exportTrailLight(choice);

    expect(downloadWorkbook).not.toHaveBeenCalled();
    expect(outcome).toMatchObject({ tone: 'error', produced: true, settled: false });
  });

  it('says plainly when nothing belongs in the file, without reading or downloading', async () => {
    generateExport.mockResolvedValue(ok(0));

    const outcome = await exportTrailLight({ ...choice, file: 'Quarterly', from: '', to: '' });

    expect(loadExportRecords).not.toHaveBeenCalled();
    expect(outcome).toEqual({
      tone: 'error',
      message: 'No case belongs in the quarterly file, so there is nothing to download.',
      produced: true,
      settled: true,
    });
  });

  it('reports a refusal and keeps the intent for a retry', async () => {
    generateExport.mockResolvedValue({ ok: false });

    const outcome = await exportTrailLight(choice);

    expect(outcome).toEqual({ tone: 'error', message: 'refused', produced: false, settled: false });
  });
});
