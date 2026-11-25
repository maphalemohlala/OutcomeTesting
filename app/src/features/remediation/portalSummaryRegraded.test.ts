import { describe, expect, it } from 'vitest';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';

/**
 * Reported 2026-11-25 on 254399947: Pass with issues regraded to Pass showed only in the
 * Regraded outcome card, while the case summary at the top still read Initial outcome alone.
 */
describe('the remediation page summary after a regrade', () => {
  const summary = remediation.slice(
    remediation.indexOf('<section class="ot-card ot-rem-summary"'),
    remediation.indexOf('</section>', remediation.indexOf('<section class="ot-card ot-rem-summary"')),
  );

  it('shows the regraded outcome from the newest outcome row, beside the initial one', () => {
    expect(summary).toContain('<dt>Initial outcome</dt>');
    expect(summary).toContain('<dt>Regraded outcome</dt>');
    expect(summary).toContain("{% include 'OT Outcome Label' final: o.al_finaloutcome %}");
    expect(summary).toContain("{% include 'OT Status Badge' status: ot_sum_final %}");
    expect(summary).toContain("{% if ot_sum_final == '' %} hidden{% endif %}");
  });

  it('fills it in place when a regrade is recorded on the page', () => {
    const script = remediation.slice(remediation.indexOf('regraded: function (label)'));
    expect(script).toContain("document.querySelector('[data-ot-summary-regraded-value]')");
    expect(script).toContain('badge(summaryValue, label);');
    expect(script).toContain('summary.hidden = false;');
  });
});
