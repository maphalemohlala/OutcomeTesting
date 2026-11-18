import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { QuarterSelect } from './QuarterSelect';

const quarters = [{ from: '2026-10-01', to: '2026-12-31', label: 'Oct–Dec 2026' }];

describe('the Quarter filter', () => {
  it('names the empty choice for what it does: every case', () => {
    const html = renderToStaticMarkup(
      <QuarterSelect quarters={quarters} from="2026-10-05" to="2026-10-20" onChoose={() => {}} />,
    );
    expect(html).toContain('<option value="">Any (every case)</option>');
    // Custom dates is what is showing, and cannot be chosen to clear them.
    expect(html).toMatch(/<option value="custom" disabled=""[^>]*>Custom dates<\/option>/);
    expect(html).not.toMatch(/<option value="">Custom dates/);
  });

  it('offers no Custom dates choice when no dates are typed', () => {
    const html = renderToStaticMarkup(
      <QuarterSelect quarters={quarters} from="" to="" onChoose={() => {}} />,
    );
    expect(html).not.toContain('Custom dates');
  });
});
