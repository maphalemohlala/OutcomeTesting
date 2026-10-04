import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';

/**
 * The portal's case header names each person by name AND email (owner, 2026-10-02: two people
 * can share a name). Read as text: the template is Liquid nothing here compiles.
 */
describe('the portal case header people', () => {
  it('reads both emails with the case', () => {
    expect(reviewTemplate).toContain('<attribute name="al_adviseremail" />');
    expect(reviewTemplate).toContain('<attribute name="al_paraplanneremail" />');
  });

  it('offers an email box beside each name', () => {
    expect(reviewTemplate).toContain('data-ot-hdr="al_adviseremail"');
    expect(reviewTemplate).toContain('data-ot-hdr="al_paraplanneremail"');
    expect(reviewTemplate).toContain('type="email" class="ot-hdr-input" data-ot-hdr="al_adviseremail"');
  });

  it('sends a person\'s name and email together when either changed', () => {
    expect(reviewTemplate).toContain("var PERSON_PAIRS = [['al_advisername', 'al_adviseremail'], ['al_paraplanner', 'al_paraplanneremail']];");
  });

  it('explains an unassigned action by the email, not the name', () => {
    expect(remediation).toContain('is held by no portal contact');
    expect(remediation).not.toContain('is not a portal contact');
  });

  it('counts ACTIVE contacts sharing the adviser email, by .size rather than entities[0]', () => {
    expect(remediation).toContain('<condition attribute="emailaddress1" operator="eq" value="{{ ot_case_adviser_email | xml_escape }}" />');
    expect(remediation).toContain('ot_adviser_holders = adviserholders.results.entities.size');
  });

  it('tells two contacts sharing the email apart from no contact holding it', () => {
    expect(remediation).toContain('{% if ot_adviser_holders > 1 %}is shared by two portal contacts{% else %}is held by no portal contact{% endif %}');
  });
});
