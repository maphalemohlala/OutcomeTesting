import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { CaseOutcomeSummary } from './CaseOutcomeSummary';

// A Tax-only case keeps its grade on the case (al_taxoutcome) and has no al_outcome row
// (TEST case 258700047, 2026-11-26), so the card must not call it ungraded.
describe('CaseOutcomeSummary Tax grade', () => {
  it('shows the Tax outcome when the case has no AQS outcome', () => {
    const html = renderToStaticMarkup(
      <CaseOutcomeSummary state={{ status: 'ready', outcomes: [] }} taxOutcome="Pass with issues" />,
    );
    expect(html).toContain('Tax check');
    expect(html).toContain('Pass with issues');
    expect(html).not.toContain('No outcome has been recorded');
  });

  it('still says no outcome when neither check has graded the case', () => {
    const html = renderToStaticMarkup(<CaseOutcomeSummary state={{ status: 'ready', outcomes: [] }} taxOutcome={null} />);
    expect(html).toContain('No outcome has been recorded');
  });
});
