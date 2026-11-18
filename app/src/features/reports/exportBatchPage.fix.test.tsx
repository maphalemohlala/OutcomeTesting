import { renderToStaticMarkup } from 'react-dom/server';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

// Owner, 2026-11-17: the daily file holds each failing case once, on its check day, whether or
// not it has closed since; the quarterly file still holds closed cases only.
const row = (id: string, checked: string, open: boolean, advice = 'Pass', file = 'Pass') => ({
  id,
  batchId: 'b1',
  caseId: null,
  caseReference: id,
  name: id,
  client: '',
  adviser: '',
  adviceGrade: advice,
  record: {
    al_initialadvicequalitygrade: advice,
    al_advicequalitygrade: advice,
    al_filequalitygrade: file,
    al_checkdate: checked,
    al_separator: open ? 'Open' : '',
  },
});

// Generated the next morning, so its default daily file is the 17th.
let generatedOn = '2026-11-18T08:00:00Z';
let earlier: { id: string; status: string; name: string; generatedOn: string; rowCount: number }[] = [];
let records = [
  row('FAILED-TODAY-OPEN', '2026-11-17', true, 'Insufficient evidence'),
  row('PASSED-TODAY', '2026-11-17', false),
  row('FAILED-LAST-MONTH', '2026-10-14', false, 'Potential harm'),
];

vi.mock('./useExports', () => ({
  useExportBatch: () => ({
    status: 'ready',
    batches: [{ id: 'b1', status: 'Generated', name: 'Batch', generatedOn, rowCount: records.length }, ...earlier],
  }),
  useExportRecords: () => ({ status: 'ready', records }),
}));

const { ExportBatchPage } = await import('./ExportBatchPage');

function page(query = ''): string {
  return renderToStaticMarkup(
    <MemoryRouter initialEntries={[`/exports/b1${query}`]}>
      <Routes>
        <Route path="/exports/:batchId" element={<ExportBatchPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

const count = (html: string, text: string) => html.split(text).length - 1;

describe('the daily file holds each fail once, on its check day', () => {
  it('holds a case failed today and still in remediation, once', () => {
    const html = page();

    expect(count(html, 'FAILED-TODAY-OPEN')).toBe(1);
    expect(html).not.toContain('PASSED-TODAY');
    expect(html).toContain('those checked on 17 Nov 2026');
  });

  it('does not hold a closed case from last month again', () => {
    expect(page()).not.toContain('FAILED-LAST-MONTH');
  });

  it('does not hold yesterday\'s fail in tomorrow\'s file', () => {
    generatedOn = '2026-11-19T08:00:00Z';
    const html = page();
    generatedOn = '2026-11-18T08:00:00Z';

    expect(html).not.toContain('FAILED-TODAY-OPEN');
    expect(html).toContain('were checked on 18 Nov 2026, so there is nothing to download.');
  });

  it('still takes the dates chosen on the page over the day the batch was made', () => {
    const html = page('?from=2026-10-01&to=2026-10-31');

    expect(html).toContain('FAILED-LAST-MONTH');
    expect(html).not.toContain('FAILED-TODAY-OPEN');
  });
});

describe("Monday's default daily file", () => {
  it("starts on Friday, the day the previous batch was generated, so the weekend's fails are in it", () => {
    const before = { generatedOn, records };
    generatedOn = '2026-11-16T09:00:00Z';
    earlier = [{ id: 'fri', status: 'Generated', name: 'Fri', generatedOn: '2026-11-13T09:00:00Z', rowCount: 0 }];
    records = [row('FAILED-SATURDAY', '2026-11-14', false, 'Potential harm'), row('FAILED-THURSDAY', '2026-11-12', false, 'Potential harm')];
    const html = page();
    ({ generatedOn, records } = before);
    earlier = [];

    expect(html).toContain('FAILED-SATURDAY');
    expect(html).not.toContain('FAILED-THURSDAY');
    expect(html).toContain('checked 13 Nov 2026 to 15 Nov 2026');
  });
});

describe('the quarterly file is unchanged', () => {
  it('holds every closed case, pass or fail, and none that was still open', () => {
    const html = page('?file=quarterly');

    expect(html).toContain('PASSED-TODAY');
    expect(html).toContain('FAILED-LAST-MONTH');
    expect(html).not.toContain('FAILED-TODAY-OPEN');
    expect(html).toContain('The file will contain 2 of the 3 cases in this batch, leaving out 1 case that does not belong in it.');
  });

  it('says so in plain words when every case in range was still open', () => {
    const before = records;
    records = [row('FAILED-TODAY-OPEN', '2026-11-17', true, 'Insufficient evidence')];
    const html = page('?file=quarterly');
    records = before;

    expect(html).toContain('belongs in the quarterly file');
    expect(html).not.toContain('daily file, so');
  });
});

describe('the Quarter filter on the daily file', () => {
  it('names the empty choice for what it does there: the days since the previous batch', () => {
    expect(page()).toContain('<option value="" selected="">Since the previous batch</option>');
    expect(page('?file=quarterly')).toContain('<option value="" selected="">Any (every case)</option>');
  });
});
