import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The rich-text answer's Clear button (Tax Remedial, the one rich-text question in V8).
 * It used to run execCommand('removeFormat'), which only unbolds the selection: with the
 * caret merely in the field it changed nothing, so a checker pressing Clear saw the text stay.
 * Read as text, not executed - the template is Liquid that nothing here compiles.
 */
describe('the rich-text answer Clear button', () => {
  const toolbar = reviewTemplate.slice(
    reviewTemplate.indexOf('<div class="ot-rte__bar"'),
    reviewTemplate.indexOf('<div class="ot-rte__area"'),
  );

  it('is not the removeFormat command', () => {
    expect(toolbar).not.toContain('removeFormat');
    expect(toolbar).toContain('<button type="button" class="ot-rte__btn" data-ot-rte-clear title="Clear the answer" aria-label="Clear the answer">Clear</button>');
  });

  it('empties the editor through the undo stack and saves the empty answer', () => {
    const handler = reviewTemplate.slice(reviewTemplate.indexOf("root.querySelector('[data-ot-rte-clear]')"));
    expect(handler).toContain("document.execCommand('selectAll', false, null);");
    expect(handler).toContain("document.execCommand('delete', false, null);");
    expect(handler.slice(0, handler.indexOf('area.addEventListener(\'paste\''))).toContain('queue();');
  });
});
