import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The check form decides nothing for the checker (project owner, 2026-10-05: "Remove any parts
 * of the form that are automatically greyed out or selected by default" - leave them open,
 * allow any answer). Read as text: the template is Liquid and script nothing compiles.
 */
describe('the check form decides nothing for the checker', () => {
  it('renders no option locked by the server', () => {
    expect(reviewTemplate).not.toContain('locked_values');
    expect(reviewTemplate).not.toContain('form_insufficient');
    expect(reviewTemplate).not.toContain('form_nofail');
    expect(reviewTemplate).not.toContain('form_fq_outcome');
  });

  it('locks, unticks and pre-ticks nothing from the script', () => {
    for (const name of ['lockOptions', 'syncGradeOptions', 'syncFailPoints', 'impliedRemedial']) {
      expect(reviewTemplate, name).not.toContain(name);
    }
  });

  it('no longer tells the checker an answer was cleared', () => {
    expect(reviewTemplate).not.toContain('Grade cleared');
    expect(reviewTemplate).not.toContain('File quality outcome cleared');
    expect(reviewTemplate).not.toContain('so there are no fail points to record');
  });

  it('still hides the root cause on a Pass, which is hiding and not greying out', () => {
    expect(reviewTemplate).toContain('var syncRootCause = function () {');
  });

  it('still disables every control once the review is submitted', () => {
    expect(reviewTemplate).toContain('{% unless editable %} disabled{% endunless %}');
  });
});
