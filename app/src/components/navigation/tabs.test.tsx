import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { Tabs, type TabItem } from './Tabs';

const items: TabItem[] = [
  { id: 'details', label: 'Details', render: () => 'details panel' },
  { id: 'history', label: 'History', render: () => 'history panel' },
];

describe('Tabs', () => {
  it('opens on the first tab when nothing outside chooses one', () => {
    const html = renderToStaticMarkup(<Tabs label="Case" items={items} />);
    expect(html).toContain('details panel');
    expect(html).not.toContain('history panel');
  });

  it('shows the tab a header button chose', () => {
    const html = renderToStaticMarkup(<Tabs label="Case" items={items} activeId="history" onChange={() => {}} />);
    expect(html).toContain('history panel');
    expect(html).toMatch(/id="tab-history"[^>]*aria-selected="true"|aria-selected="true"[^>]*id="tab-history"/);
  });

  it('falls back to the first tab when the chosen one is not there', () => {
    const html = renderToStaticMarkup(<Tabs label="Case" items={items} activeId="gone" onChange={() => {}} />);
    expect(html).toContain('details panel');
  });
});
