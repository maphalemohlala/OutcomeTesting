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
    // The queue's filters are written once and shared with the tab counts (2026-11-25).
    const filters = /\{% capture queue_filters %\}([\s\S]*?)\{% endcapture %\}/.exec(reviewList)?.[1] ?? '';
    expect(queue).toContain('{{ queue_filters }}');
    expect(filters).toContain('<condition attribute="al_aqsqueueaccountid" operator="not-null" />');
    expect(queue).not.toContain('al_reviewinstance');
    expect(filters).not.toContain('al_reviewinstance');
    expect(queue).toContain('<order attribute="al_aqsqueuedon" />');
  });

  it('names the five AQS groups AR-03 lists, as tabs (2026-11-25)', () => {
    for (const tab of [
      "'Tax review completed'",
      "'New'",
      "'Allocated to me'",
      "'In progress'",
      "'Completed'",
    ]) {
      expect(reviewList).toContain(`{% assign t_label = ${tab} %}`);
    }
  });

  it('gives Tax reviews In progress and Submitted tabs', () => {
    expect(reviewList).toContain("{% assign tab_keys = 'progress,submitted' | split: ',' %}");
    expect(reviewList).toContain("{% assign t_label = 'Submitted' %}");
  });

  it('keeps every filter on every tab, and the tab on every filter', () => {
    expect(reviewList).toContain('<input type="hidden" name="tab" value="{{ f_tab }}">');
    expect(reviewList).toContain('href="?{{ scope_qs }}tab={{ t }}"');
    // Each fetch, the tab counts included, carries the shared filters.
    expect(reviewList.match(/\{\{ review_filters \}\}/g)).toHaveLength(2);
    expect(reviewList.match(/\{\{ queue_filters \}\}/g)).toHaveLength(2);
    for (const filters of ['review_filters', 'queue_filters']) {
      const body = new RegExp(`\\{% capture ${filters} %\\}([\\s\\S]*?)\\{% endcapture %\\}`).exec(reviewList)?.[1] ?? '';
      expect(body).toContain("{% if f_ref != '' %}");
      expect(body).toContain("{% if f_route != '' %}");
      expect(body).toContain("{% if f_check != '' %}");
    }
  });

  it('shows both ages in working days', () => {
    expect(reviewList).toContain('In outcome testing');
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
