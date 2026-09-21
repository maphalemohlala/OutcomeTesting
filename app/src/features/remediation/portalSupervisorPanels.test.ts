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

/**
 * Line endings normalised before anything is matched. The working tree carries CRLF under
 * git's autocrlf while the repository holds LF, so a multi-line expectation written with \n
 * passes or fails according to how the file was checked out rather than according to what
 * the template says. That is a test reporting on the wrong thing.
 */
const remediation = remediationTemplate.replace(/\r\n/g, '\n');
const reviewList = reviewListTemplate.replace(/\r\n/g, '\n');

describe('the supervisor-only panels on the remediation page', () => {
  it('reads the role off the list the portal resolved, not off anything the page was told', () => {
    // user.roles is what the access rules are evaluated against. A claim in the markup, a
    // query string or a data- attribute would all be the page deciding who somebody is.
    expect(remediation).toContain(`{% assign signoff_role = '${SUPERVISOR_ROLE}' %}`);
    expect(remediation).toContain(
      '{% if user.roles contains signoff_role %}{% assign has_signoff_role = true %}{% endif %}',
    );
  });

  it('defaults to hiding, so a page that cannot resolve roles offers nothing', () => {
    // The order matters: false first, then set true only on a match. Written the other way
    // round, a failure to read user.roles would leave the panel showing.
    const defaultAt = remediation.indexOf('{% assign has_signoff_role = false %}');
    const grantAt = remediation.indexOf('{% assign has_signoff_role = true %}');
    expect(defaultAt).toBeGreaterThan(-1);
    expect(grantAt).toBeGreaterThan(defaultAt);
  });

  it('gates the sign-off panel on the role as well as on there being something to sign', () => {
    expect(remediation).toContain("{% if signoff_ids != '' and can_signoff %}");
    // The old gate, which offered the form to everyone.
    expect(remediation).not.toContain("{% if signoff_ids != '' %}\n          <div");
  });

  it('gates the regrade panel on the SAME rule as the sign-off, because it now has it', () => {
    // This used to read can_regrade, on the true reasoning that RegradeRequestPlugin checked
    // the role alone and gating on the mapping would hide a control the server accepted.
    // AD-202 fixed the command instead: both now run SupervisorMapping.EnsureManagesCase, so
    // one variable is the honest mirror and two would re-open the hole on the page.
    expect(remediation).toContain(
      '{% if outcome and outcome.al_initialoutcome and ot_at_recheck and can_signoff %}',
    );
    expect(remediation).not.toContain('can_regrade');
  });

  it('keeps the role flag for WORDING only, never as a gate', () => {
    // has_signoff_role picks which sentence a refused reader sees. If it ever appears in a
    // panel condition again, the page has drifted from the commands.
    const gateUses = remediation.match(/and can_signoff %\}/g) || [];
    expect(gateUses.length).toBe(2);
    expect(remediation).toContain('{% if has_signoff_role %}');
  });

  it('says why rather than showing nothing at all', () => {
    // A panel that vanishes reads as "there is nothing to do here", which is wrong: there IS
    // something to do and somebody else has to do it. The review list makes the same
    // distinction for the AQS Reviewer role.
    expect(remediation).toContain(
      'Signing off needs\n                the {{ signoff_role | escape }} role, which your account does not hold.',
    );
  });

  it('matches the pattern the review list already uses', () => {
    // One idiom for this on the site, not two. If the review list's changes, this test is
    // the thing that says the remediation page has to follow it.
    expect(reviewList).toContain(
      '{% if claim_enabled and user.roles contains claim_role %}{% assign can_claim = true %}{% endif %}',
    );
  });
});

/**
 * And a supervisor sees the sign-off form only on a case whose adviser they are the T&C
 * Manager for (project owner, 2026-09-21: "Supervisors should only see the sign off controls
 * on cases advisers linked to the handled", after finding that "service accounts holds the
 * T&C role but they are not linked to the adviser handling the case").
 *
 * The server settled this first - SignoffRequestPlugin.EnsureMappedToCase - and these pin
 * that the page now says the same thing rather than offering a form that cannot succeed.
 * The regrade panel is deliberately NOT included: its command carries no mapping gate.
 */
describe('the sign-off panel and the case adviser', () => {
  it('asks al_advisermapping whether the reader manages THIS case adviser', () => {
    expect(remediation).toContain('<entity name="al_advisermapping">');
    expect(remediation).toContain(
      '<condition attribute="al_adviseremail" operator="eq" value="{{ ot_case_adviser_email | xml_escape }}" />',
    );
  });

  it('names the manager in the filter rather than trusting the permission scope to do it', () => {
    // The table permission is contact-scoped, so the rows come back already narrowed to this
    // reader. That is the PRIVACY control. This condition is the ANSWER, and it holds even if
    // the scope is ever widened, or if a Liquid fetch turns out not to enforce one - the sort
    // of assumption AD-195 caught being wrong about this very data model.
    expect(remediation).toContain(
      '<condition attribute="al_tcmanagerid" operator="eq" value="{{ user.id | xml_escape }}" />',
    );
  });

  it('carries the adviser email on the case fetch, since the mapping is keyed on it', () => {
    const fetchStart = remediation.indexOf('{% fetchxml remcase %}');
    const fetchEnd = remediation.indexOf('{% endfetchxml %}', fetchStart);
    expect(fetchStart).toBeGreaterThan(-1);
    expect(remediation.slice(fetchStart, fetchEnd)).toContain(
      '<attribute name="al_adviseremail" />',
    );
  });

  it('defaults to not being the manager, so a failed or skipped fetch signs nothing off', () => {
    const defaultAt = remediation.indexOf('{% assign ot_is_case_tcmanager = false %}');
    const grantAt = remediation.indexOf('{% assign ot_is_case_tcmanager = true %}');
    expect(defaultAt).toBeGreaterThan(-1);
    expect(grantAt).toBeGreaterThan(defaultAt);
  });

  it('needs the role AND the mapping for the sign-off panel', () => {
    expect(remediation).toContain(
      '{% if user.roles contains signoff_role and ot_is_case_tcmanager %}{% assign can_signoff = true %}{% endif %}',
    );
  });

  it('tells a supervisor on another adviser case why, without naming the manager', () => {
    // Who supervises whom is not this reader's business - the contact-scoped permission means
    // the page could not name them anyway, and "ask Jane" would leak the very structure the
    // scope exists to keep.
    expect(remediation).toContain(
      'Signing a case off is the {{ signoff_role | escape }} mapped to its adviser,\n                which your account is not for this one.',
    );
  });

  it('skips the fetch when the case carries no adviser email', () => {
    // An `eq` against an empty value is the F48 shape. There is nothing to match either:
    // TcManagerRouting returns NoAdviserEmail for the same case, so the server refuses it too.
    expect(remediation).toContain(
      "{% if c and ot_case_adviser_email != '' and user %}",
    );
  });
});
