import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import remediationTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';
import lifecycleSource from '../../../../plugins/OutcomeTesting.Plugins/CaseLifecycle.cs?raw';

/**
 * What the portal says happens next, after a checker submits, an adviser signs off and a
 * T&C Manager decides (reported 2026-10-07: "the wording sometimes suggests that a case is
 * going to T&C or adviser even when that isn't the case").
 *
 * The pages used to PREDICT the destination: "sent to your supervisor" after every adviser
 * sign-off, although a Pass with issues closes with no sign-off and an action still open
 * moves nothing; "once the case reaches its recheck" after every approval, although a waived
 * recheck closes at once and a Tax check goes back to the queue for AQS; and "reopening or
 * regrading is a T&C Manager action" as the only words after a submit. Each page now asks
 * Dataverse where the case actually is after the write, and says that.
 */

const review = reviewTemplate.replace(/\r\n/g, '\n');
const remediation = remediationTemplate.replace(/\r\n/g, '\n');

function lifecycle(name: string): number {
  const match = new RegExp(`public const int ${name} = (\\d+);`).exec(lifecycleSource);
  if (!match) {
    throw new Error(`CaseLifecycle.${name} not found`);
  }
  return Number(match[1]);
}

const STATUS_KEYS: Record<string, string> = {
  queued: 'Queued',
  awaitingRemediation: 'AwaitingRemediation',
  remediationInProgress: 'RemediationInProgress',
  awaitingSignoff: 'AwaitingSignoff',
  awaitingRecheck: 'AwaitingRecheck',
  closed: 'Closed',
};

describe.each([
  ['OT Review Detail', review],
  ['OT Remediation', remediation],
])('%s reads the case status from Dataverse after the write', (_name, page) => {
  it('asks the Web API, not the render cache', () => {
    expect(page).toContain("'/_api/al_outcomecases(' + caseId + ')?$select=al_casestatus'");
  });

  it.each(Object.entries(STATUS_KEYS))('names %s with the value CaseLifecycle.cs holds', (key, name) => {
    expect(page).toContain(`${key}: ${lifecycle(name)}`);
  });
});

describe('after a checker submits', () => {
  it('says where the case went, for each place a submit can leave it', () => {
    expect(review).toContain('The case passed and is now closed.');
    expect(review).toContain('The case goes back to the queue for its AQS check.');
    expect(review).toContain('The case goes to the adviser to put right the points you marked down.');
  });

  it('no longer leads with the T&C Manager, which read as a hand-off', () => {
    expect(review).not.toContain('reopening or regrading is a T&C Manager action');
    expect(review).not.toContain('Reopening or regrading is a T&amp;C Manager action');
    expect(review).toContain('only a T&C Manager can reopen or regrade it');
    expect(review).toContain('only a T&amp;C Manager can reopen or regrade it');
  });
});

describe('after an adviser signs off', () => {
  it('no longer tells every adviser it went to their supervisor', () => {
    expect(remediation).not.toContain('recorded against your answer and sent to your supervisor.');
    expect(remediation).not.toContain('and sends the remediation to your supervisor.');
  });

  it('says what actually happened', () => {
    expect(remediation).toContain('The remediation is now with your T&C Manager for sign-off.');
    expect(remediation).toContain("Nothing on this grading needs your T&C Manager's sign-off, so the case is now closed.");
    expect(remediation).toContain('Other remedial actions on this case are still open; it moves on once every one is complete.');
  });
});

describe('after a T&C Manager approves', () => {
  it('no longer promises a recheck that may not be coming', () => {
    expect(remediation).not.toContain('once the case reaches its'
      + "'\n            + ' recheck, which closes the case.");
    expect(remediation).not.toContain("' The final outcome is recorded separately, on this page, once the case reaches'");
  });

  it('says where the approval left the case', () => {
    expect(remediation).toContain('The case is now closed.');
    expect(remediation).toContain('The case is at Awaiting Recheck: record the final outcome on this page to close it.');
    expect(remediation).toContain('The Tax check is finished, and the case goes back to the queue for its AQS check.');
    expect(remediation).toContain('Other remedial actions on this check are still waiting for a decision.');
  });
});

describe("the remediation page's mirror of the sign-off rule", () => {
  it('counts an AQS check still owed as due, as CompleteRemediationPlugin.SignoffRequired does', () => {
    expect(remediation).toContain('<attribute name="al_requiresaqsreview" />');
    expect(remediation).toContain("{% if c['rt.al_requiresaqsreview'] and caseoutcomes.results.entities.size == 0 %}{% assign ot_signoff_due = true %}{% endif %}");
  });
});
