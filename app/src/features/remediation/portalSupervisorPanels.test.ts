import { describe, expect, it } from 'vitest';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import reviewListTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';

/**
 * The two controls that belong to the T&C Supervisor are offered only to a T&C Supervisor
 * (project owner, 2026-09-21: "If i dont have the permission to do this, then I should not
 * see these controls").
 *
 * Both used to render for anyone, on the reasoning that hiding by role in Liquid "would look
 * like access control and enforce nothing". The first half is true and the conclusion does
 * not follow: the server is still the boundary either way, and a form somebody is refused
 * for filling in has spent their time for nothing. OT Review List had already settled the
 * right pattern for the AQS Reviewer role; these pin that the remediation page now uses it.
 *
 * What these DO NOT pin is that the role check is a security control. It is not, and must
 * never be read as one - the server-side checks below are, and they are tested where they
 * live (SignoffRequestPluginTests, RegradeRequestPlugin).
 */

const SUPERVISOR_ROLE = 'AL Portal - T&C Supervisor';

describe('the supervisor-only panels on the remediation page', () => {
  it('reads the role off the list the portal resolved, not off anything the page was told', () => {
    // user.roles is what the access rules are evaluated against. A claim in the markup, a
    // query string or a data- attribute would all be the page deciding who somebody is.
    expect(remediationTemplate).toContain(`{% assign signoff_role = '${SUPERVISOR_ROLE}' %}`);
    expect(remediationTemplate).toContain(
      '{% if user.roles contains signoff_role %}{% assign can_signoff = true %}{% endif %}',
    );
  });

  it('defaults to hiding, so a page that cannot resolve roles offers nothing', () => {
    // The order matters: false first, then set true only on a match. Written the other way
    // round, a failure to read user.roles would leave the panel showing.
    const defaultAt = remediationTemplate.indexOf('{% assign can_signoff = false %}');
    const grantAt = remediationTemplate.indexOf('{% assign can_signoff = true %}');
    expect(defaultAt).toBeGreaterThan(-1);
    expect(grantAt).toBeGreaterThan(defaultAt);
  });

  it('gates the sign-off panel on the role as well as on there being something to sign', () => {
    expect(remediationTemplate).toContain("{% if signoff_ids != '' and can_signoff %}");
    // The old gate, which offered the form to everyone.
    expect(remediationTemplate).not.toContain("{% if signoff_ids != '' %}\n          <div");
  });

  it('gates the regrade panel on the same role', () => {
    // The supervisor's other control. Left visible, the rule would look arbitrary rather
    // than considered - and a regrade is refused for exactly the same reason a sign-off is.
    expect(remediationTemplate).toContain(
      '{% if outcome and outcome.al_initialoutcome and ot_at_recheck and can_signoff %}',
    );
  });

  it('says why rather than showing nothing at all', () => {
    // A panel that vanishes reads as "there is nothing to do here", which is wrong: there IS
    // something to do and somebody else has to do it. The review list makes the same
    // distinction for the AQS Reviewer role.
    expect(remediationTemplate).toContain(
      'Signing off needs\n              the {{ signoff_role | escape }} role, which your account does not hold.',
    );
  });

  it('matches the pattern the review list already uses', () => {
    // One idiom for this on the site, not two. If the review list's changes, this test is
    // the thing that says the remediation page has to follow it.
    expect(reviewListTemplate).toContain(
      '{% if claim_enabled and user.roles contains claim_role %}{% assign can_claim = true %}{% endif %}',
    );
  });
});
