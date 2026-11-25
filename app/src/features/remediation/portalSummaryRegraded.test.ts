import { describe, expect, it } from 'vitest';
import caseDetail from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
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

describe('the case page summary after a regrade', () => {
  const start = caseDetail.indexOf('<section class="ot-card ot-rem-summary ot-case-summary"');
  const summary = caseDetail.slice(start, caseDetail.indexOf('</section>', start));

  it('shows the initial and the regraded outcome once a regrade exists', () => {
    expect(summary).toContain("{% include 'OT Outcome Label' final: sum_outcome.al_finaloutcome %}");
    expect(summary).toContain("{% if sum_regraded != '' %}");
    expect(summary).toContain('<dt>Initial outcome</dt>');
    expect(summary).toContain("{% include 'OT Outcome Label' initial: sum_outcome.al_initialoutcome %}");
    expect(summary).toContain('<dt>Regraded outcome</dt>');
  });

  it('keeps the single Outcome cell, with the Tax fallback, until then', () => {
    const otherwise = summary.slice(summary.indexOf('<dt>Regraded outcome</dt>'));
    expect(otherwise.indexOf('{% else %}')).toBeLessThan(otherwise.indexOf('<dt>Outcome</dt>'));
    expect(otherwise).toContain('<dt>Outcome</dt>');
    expect(otherwise).toContain('Tax: {{ sum_tax }}');
  });
});
