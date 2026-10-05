import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * The answer rows still carry their question and section codes, and the case header stays
 * editable after the Tax check. The gating these tests used to pin was withdrawn on
 * 2026-10-05; portalOpenForm.test.ts pins that it is gone.
 */

/** The answering script, which is emitted only where the checker may edit. */
const script = reviewTemplate;

describe('the rows carry what the rules are keyed on', () => {
  it('writes the question code onto every answer row', () => {
    // Everything above reads data-question-code. A row that lost it would read as a test
    // point, which is the safe direction but silently wrong for the five outcomes.
    const rows = reviewTemplate.match(/data-question-code="\{\{ qcode \| escape \}\}"/g);
    expect(rows).not.toBeNull();
    expect(rows!.length).toBeGreaterThanOrEqual(3);
  });

  it('writes the section code, which the AML and CRA trigger needs', () => {
    const rows = reviewTemplate.match(/data-section-code="\{\{ scode \| escape \}\}"/g);
    expect(rows).not.toBeNull();
    expect(rows!.length).toBeGreaterThanOrEqual(3);
  });

  it('gives the outcome lens row its code too', () => {
    // "Would a reasonable third party conclude the client was not exposed to foreseeable
    // harm?" - a No there is as much a finding as any Fail on the grid above it.
    expect(reviewTemplate).toContain('data-question-code="{{ lens_tick_code | escape }}"');
    expect(reviewTemplate).toContain('{% assign lens_tick_code = qcode %}');
  });
});

describe('the case header stays editable after the Tax check is submitted', () => {
  /*
   * The reversal of 2026-09-23: "both teams need to be able to edit the headers".
   *
   * For one day this page ran a second fetch for a submitted Tax review on the case and
   * forced the header read-only when it found one. These pin that it is gone, because a
   * leftover fetch feeding a variable nothing reads is exactly how it would come back.
   */
  it('no longer asks whether the Tax check has been submitted', () => {
    expect(reviewTemplate).not.toContain('{% fetchxml tax_submitted %}');
    expect(reviewTemplate).not.toContain('tax_has_submitted');
  });

  it('follows the review\'s own submitted state and nothing else', () => {
    // `locked` alone: read-only exactly when THIS review is submitted (PP-11), which is what
    // the server now does too - CaseHeaderRequestPlugin asks EnsureAssignedToCase and
    // nothing else about the ordinary fields.
    expect(reviewTemplate).toContain('{% assign header_locked = locked %}');
    expect(reviewTemplate).not.toContain('{% assign header_locked = true %}');
  });

  it('still renders the header read-only for a checker whose own review is submitted', () => {
    // The reversal widened WHEN the header may be edited, never the PP-11 rule above it.
    expect(reviewTemplate).toContain('{% if header_locked == false %}');
  });

  it('drops the notice that explained a restriction no longer there', () => {
    expect(reviewTemplate).not.toContain('the header is now read-only');
  });
});

describe('the Tax team selects say when the page is behind what was saved', () => {
  /*
   * Reported 2026-09-22 as the disposition "not sticking after reload". It does stick: the
   * page PATCHes contact.al_caseheaderrequest and the plug-in updates the case as the
   * application user, so the portal's render cache is not invalidated by the write and learns
   * through change tracking, which is polled (AD-094: up to fifteen minutes). The success
   * branch already said so at the moment of saving; what was missing was a message for
   * somebody who comes back a minute later to a page with nothing on it.
   */
  it('remembers the two selects and every tick list, per case, in this tab', () => {
    expect(script).toContain(
      "var HEADER_LAG_KEY = 'ot.savedHeader.' + status.getAttribute('data-case-id')",
    );
    expect(script).toContain('window.sessionStorage.getItem(HEADER_LAG_KEY)');
  });

  it('covers the tick lists, which are the fields that actually lag', () => {
    /*
     * Products are applied by association and read back through a fetch on the intersect.
     * Measured on TEST on 2026-09-22: that fetch was empty for about ten minutes after a save
     * Dataverse had already taken, while the plain columns beside it were current on the very
     * next reload. The field most likely to be reported as "not saving" was the one this
     * notice did not mention, which is how it was reported.
     */
    expect(script).toContain('if (saved.sets) {');
    expect(script).toContain("var lagSets = document.querySelectorAll('[data-ot-hdr-set]');");
    expect(script).toContain('if (setValueOf(lagSets[ls]) === saved.sets[setName]) { continue; }');

    // The write side, and the snapshot it needs, taken before the initial values are reset.
    expect(script).toContain('if (routeMayHaveMoved || setsMoved) {');
    expect(script).toContain('sets: snapshotSets');
  });

  it('compares before anything is wired, so it reads what the server rendered', () => {
    const report = script.indexOf('function reportHeaderLag()');
    const wiring = script.indexOf("if (disposition) { disposition.addEventListener('change'");

    expect(report).toBeGreaterThan(-1);
    expect(wiring).toBeGreaterThan(-1);
    expect(report).toBeLessThan(wiring);
  });

  it('forgets once the page has caught up, and after the window closes', () => {
    // Otherwise the notice would outlive the lag it describes and become noise.
    expect(script).toContain('var HEADER_LAG_WINDOW_MS = 20 * 60 * 1000;');
    expect(script).toContain('writeSavedHeader(null);');
  });

  it('does not re-apply the value it remembers', () => {
    // The rule the answers' own cache-lag notice follows: a value on screen the server did
    // not send is indistinguishable from one it did, and a reader could not tell which.
    const fn = script.slice(
      script.indexOf('function reportHeaderLag()'),
      script.indexOf('/** The changed header fields'),
    );

    expect(fn).not.toContain('.value =');
    expect(fn).toContain('not shown here yet');
  });

  it('tolerates a store that refuses to be written', () => {
    // Private browsing and a full store both throw, and the notice is a courtesy - not worth
    // breaking the save that has just succeeded.
    const fn = script.slice(
      script.indexOf('function writeSavedHeader(saved)'),
      script.indexOf('/** The label of a select'),
    );

    expect(fn).toContain('try {');
    expect(fn).toContain('catch (e)');
  });
});
