import { describe, expect, it } from 'vitest';
import caseTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * A checker's remedial action may be formatted (owner, 2026-11-23). It is stored as markup
 * that RemedialActionsRequestPlugin cleaned to HtmlSanitiser's allow-list, so the pages draw
 * it unescaped - but only when it starts with a tag. Words written before the editor are
 * plain and stay escaped, and the adviser's response is never markup.
 */
describe('a formatted remedial action on the portal', () => {
  const probe = "{% assign ot_ra_probe = '[[ot]]' | append: ot_ra %}{% if ot_ra_probe contains '[[ot]]<' %}<div class=\"ot-rte__read\">{{ ot_ra }}</div>{% else %}{{ ot_ra | escape | newline_to_br }}{% endif %}";

  // Every place each page draws the checker's words: two on the case record, one on the
  // remediation page, whose two kinds of action share one body since the 2026-11-24 redesign.
  it.each([
    ['case detail', caseTemplate, 2],
    ['remediation', remediationTemplate, 1],
  ])('is drawn as markup only behind the probe on %s', (_name, template, places) => {
    expect(template.split(probe).length - 1).toBe(places);
    expect(template.split('{% assign ot_ra = a.al_remedialaction | strip %}').length - 1).toBe(places);
    expect(template).not.toContain('{{ a.al_remedialaction | escape | newline_to_br }}');
    expect(template).not.toMatch(/\{\{\s*a\.al_remedialaction\s*\}\}/);
    expect(template).not.toMatch(/\{\{\s*a\.al_adviserresponse\s*\}\}/);
  });

  it('lets only the checker\'s words take the markup path on the review page', () => {
    expect(reviewTemplate).toContain("{% assign rr_probe = '[[ot]]' | append: rr_checker %}");
    expect(reviewTemplate.split("{% if rr_probe contains '[[ot]]<' %}<div class=\"ot-rte__read\">{{ rr_checker }}</div>{% else %}{{ rr_text | escape | newline_to_br }}{% endif %}").length - 1).toBe(2);
    expect(reviewTemplate).not.toMatch(/\{\{\s*rr_text\s*\}\}/);
    expect(reviewTemplate).not.toMatch(/\{\{\s*rr_adviser\s*\}\}/);
  });
});
