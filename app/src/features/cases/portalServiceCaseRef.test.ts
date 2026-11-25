import { describe, expect, it } from 'vitest';
import caseList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-list/OT-Case-List.webtemplate.source.html?raw';
import myWork from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-my-work/OT-My-Work.webtemplate.source.html?raw';
import reviewList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import caseDetail from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-case-detail/OT-Case-Detail.webtemplate.source.html?raw';
import reviewDetail from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import portalCss from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';

/**
 * The service case reference on the portal (client, 2026-11-24): TaskID stays the case key,
 * and ServiceCaseSequentialRef is shown beside it and found by the same search box. Read as
 * text - the templates are Liquid nothing here compiles - so these pin the markup and the
 * FetchXML the behaviour depends on.
 */
const LISTS = { caseList, myWork, reviewList, remediation };
// Cases has one search box since the 2026-11-24 redesign, over client and adviser as well.
const REF_LISTS = { myWork, reviewList, remediation };

describe('the portal case lists', () => {
  it('caseList searches the TaskID, the service case reference, the client and the adviser from one box', () => {
    const like = (column: string) =>
      `<condition attribute="${column}" operator="like" value="%{{ f_q | xml_escape }}%" />`;
    expect(caseList).toContain(
      `{% if f_q != '' %}<filter type="or">${like('al_casereference')}${like('al_servicecaseref')}${like('al_clientname')}${like('al_advisername')}</filter>{% endif %}`,
    );
    expect(caseList).toContain('<span class="ot-visually-hidden">Case reference, client or adviser</span>');
    expect(caseList).toContain('<input type="search" name="q" value="{{ f_q | escape }}"');
  });

  it.each(Object.entries(REF_LISTS))('%s searches the TaskID and the service case reference together', (_, source) => {
    // One "or" filter, so a reference matching either column finds the case.
    expect(source).toMatch(
      /<filter type="or"><condition (entityname="case" )?attribute="al_casereference" operator="like" value="%\{\{ f_ref \| xml_escape \}\}%" \/><condition (entityname="case" )?attribute="al_servicecaseref" operator="like" value="%\{\{ f_ref \| xml_escape \}\}%" \/><\/filter>/,
    );
    expect(source).not.toMatch(/<filter type="and"><condition attribute="al_casereference" operator="like"/);
    // The box is labelled for both references: a field label, or since the 2026-11-24 redesign
    // the toolbar's search label wrapping the input.
    expect(source).toMatch(
      /<label for="f-ref">Case or service case reference<\/label>|<label class="ot-search">\s*<span class="ot-visually-hidden">Case or service case reference<\/span>\s*<input type="search" id="f-ref" name="ref"/,
    );
  });

  it.each(Object.entries(LISTS))('%s fetches the service case reference and shows it under the TaskID', (_, source) => {
    expect(source).toContain('<attribute name="al_servicecaseref" />');
    expect(source).toMatch(
      /\{% if (c\.al_servicecaseref|r\['case\.al_servicecaseref'\]) %\}<span class="ot-service-ref"><span class="ot-visually-hidden">Service case reference <\/span>/,
    );
  });

  it('styles the line as supporting detail beneath the link', () => {
    expect(portalCss).toMatch(/\.ot-service-ref\s*\{[^}]*display:\s*block/);
  });
});

describe('the portal case headers', () => {
  it('draws both references side by side on the case page', () => {
    expect(caseDetail).toContain('<attribute name="al_servicecaseref" />');
    expect(caseDetail).toContain('<td class="lbl">Case reference</td><td class="val">{{ c.al_casereference | escape }}</td>');
    expect(caseDetail).toContain(
      "<td class=\"lbl\">Service case reference</td><td class=\"val\">{{ c.al_servicecaseref | default: '—' | escape }}</td>",
    );
  });

  it('draws them in both the editable and the submitted header of the review page', () => {
    expect(reviewDetail).toContain('<attribute name="al_servicecaseref" />');
    const cells = "<td class=\"lbl\">Service case reference</td><td class=\"val\">{{ rv['case.al_servicecaseref'] | default: '&mdash;' | escape }}</td>";
    expect(reviewDetail.split(cells)).toHaveLength(3);
    expect(reviewDetail.split("<td class=\"lbl\">Case reference</td><td class=\"val\">{{ rv['case.al_casereference'] | escape }}</td>")).toHaveLength(3);
  });
});
