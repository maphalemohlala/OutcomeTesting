import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

const row = (id: string, advice: string, file: string) => ({
  id,
  batchId: 'b1',
  caseId: null,
  caseReference: id,
  name: id,
  client: '',
  adviser: '',
  adviceGrade: advice,
  record: { al_advicequalitygrade: advice, al_filequalitygrade: file },
});

let recordedRows = 3;

vi.mock('./useExports', () => ({
  useExportBatches: () => ({
    status: 'ready',
    batches: [{ id: 'b1', status: 'Generated', name: 'Batch', generatedOn: null, rowCount: recordedRows }],
  }),
  useExportRecords: () => ({
    status: 'ready',
    records: [
      row('CASE-PASS', 'Pass', 'Pass'),
      row('CASE-HARM', 'Potential Harm', 'Pass'),
      row('CASE-FQFAIL', 'Pass', 'Fail'),
    ],
  }),
}));

const { ExportBatchPage } = await import('./ExportBatchPage');

function page(query: string): string {
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[`/exports/b1${query}`]}>
      <Routes>
        <Route path="/exports/:batchId" element={<ExportBatchPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('the daily and quarterly files on a batch', () => {
  it('leaves passes with no fails out of the daily file, the default', () => {
    const html = page('');

    expect(html).not.toContain('CASE-PASS');
    expect(html).toContain('CASE-HARM');
    expect(html).toContain('CASE-FQFAIL');
    expect(html).toContain('The file will contain 2 of the 3 cases in this batch, leaving out 1 case that does not belong in it.');
  });

  it('says so and blocks the download when fewer rows came back than the batch recorded', () => {
    recordedRows = 4;
    const html = page('');
    recordedRows = 3;

    expect(html).toContain('This batch recorded 4 case(s) but 3 could be read');
    expect(html).toMatch(/<button[^>]*disabled[^>]*>[^<]*Download Trail Light file/);
  });

  it('keeps every case in the quarterly file', () => {
    const html = page('?file=quarterly');

    expect(html).toContain('CASE-PASS');
    expect(html).toContain('The file will contain all 3 cases in this batch.');
  });
});
