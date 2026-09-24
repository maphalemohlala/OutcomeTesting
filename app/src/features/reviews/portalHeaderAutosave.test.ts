import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The case header autosave on OT Review Detail must not lose a change made while an earlier
 * save is still on its way (project owner, 2026-09-24, case 900000006).
 *
 * What happened: a Tax checker ticked two products and chose Sample source and Product /
 * solution type in quick succession. Each change asked for a save; the ones asked for while
 * a save was in flight were skipped, and when that save came back the page took the fields'
 * CURRENT values as its saved baseline - so the skipped changes were marked saved and never
 * sent. The audit trail shows one product and neither select reaching the case, and the AQS
 * checker then saw exactly that.
 *
 * Read as text, like the rest of this page's tests: nothing here executes the template. The
 * behaviour itself is proved in DEV by answering a header quickly and reading the case back.
 */
const script = /function saveNow\(done\) \{[\s\S]*?\n {10}\}\n/.exec(reviewTemplate)?.[0] ?? '';

describe('the case header autosave', () => {
  it('is found, so the checks below are reading the real function', () => {
    expect(script).toContain('al_caseheaderrequest');
  });

  it('does not report a save asked for mid-flight as done', () => {
    expect(script).not.toMatch(/if \(saving\) \{ done\(true\); return; \}/);
    expect(script).toMatch(/if \(saving\) \{[^}]*saveAgain = true;[^}]*waiting\.push\(done\);[^}]*return;/);
  });

  it('moves the saved baseline to what was SENT, not to what the controls hold on reply', () => {
    expect(script).not.toContain('headerInputs[k].initial = headerInputs[k].el.value;');
    expect(script).not.toContain('headerSets[m].initial = setValueOf(headerSets[m].el);');
    expect(script).toContain('headerInputs[k].initial = sentInputs[k];');
    expect(script).toContain('headerSets[m].initial = sentSets[m];');
  });

  it('sends whatever moved in the meantime once the save lands', () => {
    expect(script).toMatch(/function finish\(ok\) \{[\s\S]*saveAgain[\s\S]*changedFields\(\)[\s\S]*saveNow\(/);
  });

  it('keeps a submit waiting until every queued header change is stored', () => {
    // The submit calls saveNow(done) and only proceeds on true. A queued change must hold
    // that callback until its own save lands, or the review locks over an unsaved header.
    expect(script).toMatch(/for \(var w = 0; w < callbacks\.length; w\+\+\) \{ callbacks\[w\]\(/);
  });
});
