import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it, vi } from 'vitest';
import { ukToday } from '../cases/caseHeaderDates';
import { dayBefore } from './trailLight';

vi.mock('./useExports', () => ({
  useExportBatches: () => ({
    status: 'ready',
    batches: [
      { id: 'b1', name: 'Daily file, checked 24 Nov 2026', code: 'EXB-1', status: 'Generated', generatedOn: null, rowCount: 5 },
    ],
  }),
}));
vi.mock('./exportTrailLight', () => ({ exportTrailLight: vi.fn() }));
vi.mock('./fullExtract', () => ({ buildFullExtract: vi.fn() }));

const { ExportsPage } = await import('./ExportsPage');

describe('the Exports page is one form and one action', () => {
  const html = renderToStaticMarkup(<ExportsPage />);

  it('starts on the daily file for yesterday, the last whole UK day', () => {
    const yesterday = dayBefore(ukToday());
    expect(html).toContain('<option value="Daily" selected="">Daily file</option>');
    expect(html).toContain(`id="check-from" type="date" max="${yesterday}" value="${yesterday}"`);
    expect(html).toContain(`id="check-to" type="date" min="${yesterday}" value="${yesterday}"`);
  });

  it('says the daily file holds File Quality fails and advice fails only', () => {
    const note = html.replace(/\s+/g, ' ');
    expect(note).toContain(
      'The daily file holds only the cases with a File Quality Fail and/or an Advice Quality grade first recorded as Insufficient evidence or Potential harm.',
    );
    expect(note).toContain('A Tax-only case appears only when its File Quality is Fail.');
    expect(note).not.toContain('Pass with issues');
  });

  it('has one Export button and no batch to create or generate', () => {
    expect(html).toContain('<button type="submit" class="exports__btn">Export</button>');
    expect(html).not.toContain('New export batch');
    expect(html).not.toContain('>Generate<');
    expect(html).not.toContain('href="/exports/');
  });

  it('lists previous exports by the file they were', () => {
    expect(html).toContain('Daily file, checked 24 Nov 2026');
  });
});
