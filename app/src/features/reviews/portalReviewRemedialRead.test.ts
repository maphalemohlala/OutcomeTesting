import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * A submitted check shows the remedial actions it raised, so the page's Save as PDF carries
 * them as the emailed document does (project owner, 2026-10-05).
 *
 * The row order must match CheckRemedialActions.Actions (plugins/OutcomeTesting.Plugins/
 * CheckRemedialActions.cs): createdon, then the numeric index after the code's last '-'
 * (controller ruling, 2026-10-05) — not the fetch's own lexical order, which reads
 * 1, 10, 2, 3... once a check raises ten or more actions.
 */
const start = reviewTemplate.indexOf('{% fetchxml review_actions %}');
const section = reviewTemplate.slice(start, reviewTemplate.indexOf('</section>', start));

describe('the submitted review page lists its remedial actions', () => {
  it('reads this review\'s live actions', () => {
    expect(start).toBeGreaterThan(-1);
    expect(section).toContain('<condition attribute="al_reviewinstanceid" operator="eq" value="{{ review_id | xml_escape }}" />');
    expect(section).toContain('<condition attribute="statecode" operator="eq" value="0" />');
    expect(section).toContain('<attribute name="al_remediationactioncode" />');
  });

  it('is drawn only on a review that can no longer be edited', () => {
    const opener = reviewTemplate.lastIndexOf('{% unless editable %}', start);
    expect(opener).toBeGreaterThan(-1);
    expect(reviewTemplate.slice(opener, start)).not.toContain('{% endunless %}');
  });

  it('heads the columns as the emailed document does', () => {
    for (const heading of ['No.', 'Fail point', 'Remedial action', 'Owner', 'Target date', 'Status']) {
      expect(section).toContain(`<th scope="col">${heading}</th>`);
    }
    expect(section).toContain('>Remedial actions</h2>');
  });

  it('splits the fail points with the filters this site accepts', () => {
    expect(section).toContain("| remove_first: '- '");
    expect(section).not.toMatch(/split: '[^']{2,}'/);
    expect(section).not.toMatch(/truncate: \d+, /);
  });

  it('re-sorts the fetch result into raise order rather than trusting the lexical fetch order', () => {
    expect(section).toContain('{% for n in (1..');
  });

  it('compares the code tail as a string, since DotLiquid\'s plus filter concatenates a string input instead of adding', () => {
    // "3" | plus: 0 is string.Concat in DotLiquid when the input is a string, giving "30"
    // rather than 3 — numeric conversion would send every itemised action to the catch-all
    // pass and silently degrade the order back to the fetch's lexical one (fix round 1,
    // controller finding, 2026-10-05).
    expect(section).not.toMatch(/rr_(tail|idx)\s*\|\s*plus:/);
    expect(section).toContain('{% capture rr_n %}{{ n }}{% endcapture %}');
    expect(section).toContain('rr_tail == rr_n');
  });
});
