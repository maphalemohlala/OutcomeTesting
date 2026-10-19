import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * A fail point retired in the question library never shows on a check in progress (project
 * owner, 2026-10-19: "Ensure that retired fail points do not show when checkers perform their
 * checks"). Only a submitted review, which is locked, keeps a retired point it ticked. Read as
 * text: the template is Liquid nothing compiles.
 */
describe('retired fail points on the check page', () => {
  it('reads each point’s status', () => {
    expect(reviewTemplate).toContain('<attribute name="statecode" />');
  });

  it('draws a retired point only on a locked review that ticked it', () => {
    const rule = reviewTemplate.slice(
      reviewTemplate.indexOf('{% assign fr_show = true %}'),
      reviewTemplate.indexOf('{% if fr_show %}'),
    );
    expect(rule).toContain('{% if fr.statecode.value == 1 %}');
    expect(rule).toContain('{% assign fr_show = false %}');
    expect(rule).toContain('{% if locked and fr_ticked %}{% assign fr_show = true %}{% endif %}');
  });
});
