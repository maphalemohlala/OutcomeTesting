import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import answerOptions from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-answer-options/OT-Answer-Options.webtemplate.source.html?raw';
import myWork from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-my-work/OT-My-Work.webtemplate.source.html?raw';
import reviewList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';
import caseDetail from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import portalCss from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';
import appCss from '../../styles/document.css?raw';

/**
 * The checker feedback batch of 2026-09-24, as the portal renders it. Read as text, not
 * executed - the templates are Liquid that nothing here compiles - so these pin the markup
 * each change depends on rather than the page it produces.
 */
describe('the review page after the 2026-09-24 feedback', () => {
  it('heads a Suitability subsection by its name alone', () => {
    expect(reviewTemplate).toContain('<tr class="section"><td colspan="{{ ncols }}">{{ sname | escape }}');
    expect(reviewTemplate).not.toContain("{{ scode | remove_first: 'S-' }}. {{ sname | escape }}");
  });

  it('prints no Consumer Duty intro line', () => {
    const cd = reviewTemplate.slice(reviewTemplate.indexOf("{% elsif scode == 'S-CD' %}"));
    expect(cd.slice(0, cd.indexOf("{% elsif scode == 'S-GRADE' %}"))).toContain("{% assign blk_intro = '' %}");
  });

  it('draws the N/A scale as a four-column grid, leaving N/A empty on a plain suitability row', () => {
    expect(reviewTemplate).toContain("{% assign gvalues = '120910300,120910303,120910302,120910301,120910307' | split: ',' %}");
    expect(reviewTemplate).toContain("{% assign glabels = 'Pass,Pass with issues,Insufficient evidence,Potential harm,N/A' | split: ',' %}");
    expect(reviewTemplate).toContain("{% if blk_rt == 120910012 and rt == 120910006 %}{% assign on_scale = true %}{% endif %}");
    expect(reviewTemplate).toContain("{% if rt == 120910006 %}{% if v == '120910307' or v == '120910303' %}{% assign cell_off = true %}{% endif %}{% endif %}");
    expect(reviewTemplate).toContain('{% if suit_na %}{% assign grid_rt = 120910012 %}{% endif %}');
    expect(answerOptions).toContain('{% when 120910012 %}');
  });

  it('gives the N/A column the same share as the other tick columns, on both surfaces', () => {
    // 55% label + 4 x 15% claimed 115%, and the browser left N/A at 38px of a 1232px table.
    expect(portalCss).toContain('.ot-checklist .grid:has(thead th:nth-of-type(5)) td.optcell { width: 11.25%; }');
    expect(appCss).toContain('.checklist-doc .grid:has(thead th:nth-of-type(5)) td.optcell { width: 11.25%; }');
  });

  it('lets several root causes be ticked on the multi-select version', () => {
    expect(reviewTemplate).toContain('{% elsif rt == 120910003 or rt == 120910013 %}');
    expect(reviewTemplate).toContain('data-column="{% if rt == 120910013 %}choices{% else %}choice{% endif %}"');
    expect(reviewTemplate).toMatch(/<input type="checkbox" class="cc-box" value="\{\{ v \}\}" aria-label="\{\{ qv\.al_questiontext \| escape \}\}: \{\{ rclabels/);
  });

  it('offers a way back to the queue at the foot of the check', () => {
    expect(reviewTemplate).toContain(
      '<a class="ot-btn" href="{% if is_tax_review %}/tax-reviews{% else %}/aqs-reviews{% endif %}">Back to your queue</a>',
    );
  });

  it('reads IO reference from the ClientRef, not the case reference (F6)', () => {
    expect(reviewTemplate).toContain('<attribute name="al_ioreference" />');
    expect(reviewTemplate.match(/IO reference<\/td><td class="val">\{\{ rv\['case\.al_ioreference'\]/g)).toHaveLength(2);
    expect(reviewTemplate).not.toMatch(/IO reference<\/td><td class="val">\{\{ rv\['case\.al_casereference'\]/);
  });
});

describe('every case table says who the case is assigned to', () => {
  const checkerColumns = (template: string) =>
    template.includes('<th scope="col">Tax checker</th>') && template.includes('<th scope="col">AQS checker</th>');

  it('on My Work, both review lists and the remediation list', () => {
    // My work names both checkers in one Checkers column since the 2026-11-24 redesign, each
    // on its own line under its discipline, as Cases does.
    expect(myWork).toContain('<th scope="col">Checkers</th>');
    expect(myWork).toContain('<li><span class="ot-people__role">Tax</span>{% if tax_checker != \'\' %}');
    expect(myWork).toContain('<li><span class="ot-people__role">AQS</span>{% if aqs_checker != \'\' %}');
    expect(checkerColumns(myWork)).toBe(false);
    // The AQS queue tabs and the reviewer's own submitted table both name the checkers in
    // one Checkers column (2026-11-25 tabs), as Cases does, and the in-progress cards draw
    // the same list.
    expect(checkerColumns(reviewList)).toBe(false);
    expect(reviewList.match(/<th scope="col">Checkers<\/th>/g)).toHaveLength(2);
    expect(reviewList).toContain('<li><span class="ot-people__role">Tax</span>{% if tax_checker != \'\' %}');
    expect(reviewList).toContain('<li><span class="ot-people__role">AQS</span>{% if aqs_checker != \'\' %}');
    expect(reviewList).toContain('<td>{{ r_people }}</td>');
    // The remediation list names both checkers in one Checkers column too (2026-11-24 redesign).
    expect(remediation).toContain('<th scope="col">Checkers</th>');
    expect(remediation).toContain('<li><span class="ot-people__role">Tax</span>{% if tax_checker != \'\' %}');
    expect(remediation).toContain('<li><span class="ot-people__role">AQS</span>{% if aqs_checker != \'\' %}');
    for (const template of [myWork, reviewList, remediation]) {
      expect(template).toContain('<attribute name="al_taxcheckername" />');
      expect(template).toContain('<attribute name="al_aqscheckername" />');
    }
  });

  it('on the case record’s reviews table', () => {
    expect(caseDetail).toContain('<th scope="col">Assigned to</th>');
  });
});
