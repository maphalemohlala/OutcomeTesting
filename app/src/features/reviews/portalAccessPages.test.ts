import { describe, expect, it } from 'vitest';
import reviewList from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-list/OT-Review-List.webtemplate.source.html?raw';
import remediation from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-remediation/OT-Remediation.webtemplate.source.html?raw';

/** The portal pages under role-scoped access (AD-218, AR-02, AR-03, AR-04). */
describe('the review list', () => {
  it('lists only the signed-in reviewer\'s own reviews, with no "all" option', () => {
    expect(reviewList).toContain('<condition attribute="al_assignedcontactid" operator="eq" value="{{ user.id | xml_escape }}" />');
    expect(reviewList).not.toMatch(/f_mine == '1' %\}<condition attribute="al_assignedcontactid"/);
    expect(reviewList).not.toContain('>All {{ heading | downcase }}</a>');
  });

  it('reads the AQS queue from the queue column, not by joining reviews it cannot read', () => {
    const queue = /\{% fetchxml available %\}([\s\S]*?)\{% endfetchxml %\}/.exec(reviewList)?.[1] ?? '';
    expect(queue).toContain('<condition attribute="al_aqsqueueaccountid" operator="not-null" />');
    expect(queue).not.toContain('al_reviewinstance');
    expect(queue).toContain('<order attribute="al_aqsqueuedon" />');
  });

  it('names the five AQS groups AR-03 lists', () => {
    for (const heading of [
      'Tax review completed – awaiting allocation',
      'New – awaiting allocation',
      'Allocated to me',
      'In progress',
      'Completed',
    ]) {
      expect(reviewList).toContain(heading);
    }
  });

  it('shows both ages in working days', () => {
    expect(reviewList).toContain('Days in OTIS');
    expect(reviewList).toContain('Waiting for AQS');
    expect(reviewList).toContain('data-ot-workdays');
  });
});

describe('the remediation list', () => {
  it('offers the three adviser stages', () => {
    for (const label of ['Remedial action required', 'Awaiting T&amp;C sign-off', 'Closed']) {
      expect(remediation).toContain(label);
    }
  });
});
