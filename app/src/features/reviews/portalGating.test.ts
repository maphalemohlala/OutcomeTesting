import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * What the rest of the checklist leaves available, as OT Review Detail renders it (project
 * owner, 2026-09-22).
 *
 * `ChecklistGating` in the plug-in assembly is the authority and `ChecklistGatingTests` pins
 * it. These pin the PORTAL's copy, which is the half a checker actually sees - and which has
 * no other safety net: the template is Liquid and JavaScript that nothing compiles, and this
 * file has a recorded history of failing silently (a multi-value `when` that never matched,
 * a variable named `block` that never varied, a comment containing a tag that took the whole
 * page down).
 *
 * Read as text, not executed. The value is in catching the four things that would make the
 * page and the server disagree, each of which has a specific way of going wrong:
 *
 *   - the outcome-question exclusion drifting, which is what makes the rules contradict
 *     each other rather than merely differ;
 *   - the page deciding the opening state in the render loop, which cannot work for the
 *     file quality outcome because the document draws it before the sections it depends on;
 *   - the gating hanging off the grade row, which a Tax review does not have at all;
 *   - the rows losing the question code the whole thing is keyed on.
 */

/** The answering script, which is emitted only where the checker may edit. */
const script = reviewTemplate;

describe('the outcome questions are excluded from the scan', () => {
  /*
   * THE rule that makes the others coherent. Choosing Pass on the file quality outcome
   * defaults Remedial action required? to No; if that No counted as a finding it would take
   * the Pass straight back off, and Pass would be unreachable on every review.
   */
  const outcomeCodes = ['Q-FQ-01', 'Q-FQTAX-01', 'Q-FQ-03', 'Q-FQTAX-03', 'Q-GR-01'];

  it('names the same five codes the server excludes', () => {
    const list = /var OUTCOME_CODES = '([^']*)'/.exec(script);
    expect(list).not.toBeNull();

    const codes = list![1].split('|').filter((code) => code !== '');
    expect(codes.sort()).toEqual([...outcomeCodes].sort());
  });

  it('excludes them in the server-rendered pre-pass too', () => {
    // The page has two copies of this list - one Liquid, one JavaScript - because one decides
    // the state the page OPENS in and the other keeps up with what changes. They must agree,
    // or a reload would silently move the answer.
    const list = /assign outcome_codes = '([^']*)'/.exec(reviewTemplate);
    expect(list).not.toBeNull();

    const codes = list![1].split('|').filter((code) => code !== '');
    expect(codes.sort()).toEqual([...outcomeCodes].sort());
  });
});

describe('the opening state is decided before anything is drawn', () => {
  it('works the three flags out ahead of the render loop', () => {
    // Not in the loop. The Checker Checklist draws File Quality Outcome third, before the
    // five Suitability sections, so a flag accumulated row by row would not yet know about a
    // Fail on E3 when that outcome's options were decided - and the page would open offering
    // a Pass the server refuses.
    const prepass = reviewTemplate.indexOf('{% assign form_insufficient = false %}');
    const loop = reviewTemplate.indexOf('{% assign qcode = qv[');

    expect(prepass).toBeGreaterThan(-1);
    expect(loop).toBeGreaterThan(-1);
    expect(prepass).toBeLessThan(loop);
  });

  it('locks both grades on insufficient evidence and only Pass on a no or fail', () => {
    const block = reviewTemplate.slice(
      reviewTemplate.indexOf('{% assign locked_values = \'\' %}'),
      reviewTemplate.indexOf("{% include 'OT Answer Options'"),
    );

    // Insufficient evidence leaves only Insufficient evidence and Potential harm.
    expect(block).toContain("{% if form_insufficient %}");
    expect(block).toContain("'|120910300|120910303|'");

    // A No or a Fail takes Pass alone - Pass with issues is exactly the grade for a file that
    // failed a test point, so it must stay available.
    expect(block).toContain('{% elsif form_nofail %}');
    expect(block).toContain("'|120910300|'");
  });

  it('locks Pass on the file quality outcome of either discipline', () => {
    const block = reviewTemplate.slice(
      reviewTemplate.indexOf('{% assign locked_values = \'\' %}'),
      reviewTemplate.indexOf("{% include 'OT Answer Options'"),
    );

    expect(block).toContain("qcode == 'Q-FQ-01' or qcode == 'Q-FQTAX-01'");
  });
});

describe('the gating does not hang off the grade row', () => {
  /*
   * A Tax review has no grade at all - S-GRADE is AQS-owned and AD-020 filters it out - and
   * two of the rules are the Tax form's: its fail points lock on a Pass, and its Remedial
   * action required? takes the same default. While this block opened with `if (gradeRow)`
   * neither of them ran on the review they were written for.
   */
  it('no longer opens the block on the grade row', () => {
    // The exact opening that used to shut the Tax form out, not the string `if (gradeRow)` -
    // which still appears, and should, where the grade's OWN options are locked.
    expect(script).not.toMatch(/if \(gradeRow\) \{\s*var gradeInputs = gradeRow\.querySelectorAll/);
  });

  it('guards each rule on the row it actually needs', () => {
    expect(script).toContain('if (!rootCause || !gradeRow) { return; }');
    expect(script).toContain('var gradeStatus = gradeRow ? gradeRow.querySelector');
    expect(script).toContain('if (gradeRow) {');
  });
});

describe('the fail points', () => {
  it('lock on the TAX CHECK outcome for Tax, not the file quality one', () => {
    /*
     * The correction of 2026-09-23. Q-TAX-02 is the Tax check outcome on S-TAX; Q-FQTAX-01
     * is the tax FILE QUALITY outcome on S-FQTAX. This rule read the second until that day
     * and should always have read the first, and the checklist records that the two have
     * been conflated in code before.
     */
    const fn = script.slice(
      script.indexOf('var syncFailPoints = function (found)'),
      script.indexOf('var syncGradeOptions = function ()'),
    );

    expect(fn).toContain('isTaxReview');
    expect(fn).toContain('tickedOn(taxOutcomeRow) === GRADE_PASS');
    expect(fn).not.toContain('tickedOn(fileQualityRow)');
  });

  it('find the Tax check outcome row by its own question code', () => {
    expect(script).toContain("var taxOutcomeRow = rowFor(answerRows, '|Q-TAX-02|');");
  });

  it('lock for AQS when every AML and CRA point reads Yes', () => {
    // Reversed 2026-09-23: it used to be locked UNTIL a finding arrived, so a checker who
    // had not reached that section yet met the block shut.
    const fn = script.slice(
      script.indexOf('var syncFailPoints = function (found)'),
      script.indexOf('var syncGradeOptions = function ()'),
    );

    expect(fn).toContain('found.amlCraAllYes');
    expect(fn).not.toContain('!found.amlCra;');
  });

  it('count the AML and CRA questions rather than scanning for a finding', () => {
    /*
     * The question that decides this rule is the UNANSWERED one, which has no ticked input
     * to find. Counting has to happen per ROW, outside the loop over ticks - otherwise three
     * Yeses and two blanks read as all Yes and the block shuts on an unfinished section.
     */
    const fn = script.slice(
      script.indexOf('var findings = function ()'),
      script.indexOf('var lockOptions = function'),
    );

    expect(fn).toContain("row.getAttribute('data-section-code') === 'S-AMLCRA'");
    expect(fn).toContain('found.amlCraTotal++;');
    expect(fn).toContain("if (tickedOn(row) === YES) { found.amlCraYes++; }");

    // An empty section is not a clean one. A Tax review has no AML and CRA section at all
    // and must not come out of this locked.
    expect(fn).toMatch(
      /found\.amlCraAllYes = found\.amlCraTotal > 0\s*\n\s*&& found\.amlCraYes === found\.amlCraTotal;/,
    );
  });

  it('say why they are locked rather than just greying out', () => {
    expect(reviewTemplate).toContain('data-ot-failpoints-note');
    expect(script).toContain('The Tax check outcome is a Pass, so there are no fail points');
    expect(script).toContain('Every AML and CRA checking point reads Yes');
  });
});

describe('the remedial action lock', () => {
  /** syncRemedialAction, which replaced the default on 2026-09-23. */
  const sync = () =>
    script.slice(
      script.indexOf('var syncRemedialAction = function ()'),
      script.indexOf('for (var sr = 0; sr < answerRows.length'),
    );

  it('reads No for a Pass and Yes for a Fail', () => {
    const fn = script.slice(
      script.indexOf('var impliedRemedial = function ()'),
      script.indexOf('var syncRemedialAction = function ()'),
    );

    expect(fn).toContain('if (outcome === GRADE_PASS) { return NO; }');
    expect(fn).toContain('if (outcome === FAIL) { return YES; }');
  });

  it('disables the answer the outcome did not imply', () => {
    /*
     * THE change of 2026-09-23: "the user can still select yes manually, it needs to be
     * disabled". What the outcome implies is now the only answer the question takes, so the
     * page must take the other one away rather than merely fill this one in.
     */
    expect(sync()).toContain("var unwanted = wanted === YES ? NO : YES;");
    expect(sync()).toContain("lockOptions(remedialRow, '|' + unwanted + '|', true);");
  });

  it('unlocks before it locks, so the row is never left with neither answer', () => {
    // Moving the outcome from Pass to Fail has to OPEN Yes before No is taken away, and a
    // disabled input cannot be ticked by the lines that follow.
    const fn = sync();

    expect(fn.indexOf("lockOptions(remedialRow, '|' + wanted + '|', false);")).toBeGreaterThan(-1);
    expect(fn.indexOf("lockOptions(remedialRow, '|' + wanted + '|', false);")).toBeLessThan(
      fn.indexOf("lockOptions(remedialRow, '|' + unwanted + '|', true);"),
    );
  });

  it('leaves both answers open while no outcome is recorded', () => {
    // A checker working bottom-up reaches this question before the outcome above it on
    // plenty of files, and there is nothing yet for their answer to contradict - which is
    // what ChecklistGating.RemedialActionRefusal does with it too.
    expect(sync()).toMatch(
      /if \(wanted === ''\) \{\s*\n\s*lockOptions\(remedialRow, '\|' \+ YES \+ '\|' \+ NO \+ '\|', false\);\s*\n\s*return;/,
    );
  });

  it('no longer tries to tell a default from a hand-made choice', () => {
    /*
     * `remedialChosenByHand` and the `defaulting` re-entry flag both answered "may this be
     * overwritten?", which now has one answer. Left in place they would be a rule the page
     * still ran and nobody maintained.
     */
    // Matched on the code rather than on the word: the comment above syncRemedialAction
    // names both by hand, saying what they were and why they went, and that is the one place
    // they should still be readable.
    expect(script).not.toMatch(/remedialChosenByHand\s*=/);
    expect(script).not.toContain('var defaulting = false;');
    expect(script).not.toContain('var defaultRemedialAction');
  });

  it('saves through the answer autosave rather than writing on its own', () => {
    // Setting `checked` in script raises no event, and the autosave listens for `change`.
    // Dispatching it means the tick is saved, audited and refused on exactly the path a
    // checker's own click takes - and it is also what repairs a review already holding the
    // contradiction, where lockOptions has just unticked the stored answer.
    expect(sync()).toContain("event.initEvent('change', true, false)");
    expect(sync()).toContain('dispatchEvent(event)');
  });

  it('runs at load as well as on change', () => {
    // A default only had to act when the outcome moved. A lock has to hold on a page opened
    // against an answer already saved - including one saved before the rule existed.
    expect(script).toContain('syncRemedialAction();');
    expect(script).toContain("fqInputs[fi].addEventListener('change', syncRemedialAction);");
  });

  it('is rendered locked by the server too, before any script runs', () => {
    // So the page arrives with the right option disabled, and so it is still right on a
    // read-only review, where no script is emitted at all.
    const block = reviewTemplate.slice(
      reviewTemplate.indexOf('{% assign locked_values = \'\' %}'),
      reviewTemplate.indexOf("{% include 'OT Answer Options'"),
    );

    expect(block).toContain("qcode == 'Q-FQ-03' or qcode == 'Q-FQTAX-03'");
    expect(block).toContain('{% if form_fq_outcome == 120910300 %}');
    expect(block).toContain("{% assign locked_values = '|120910305|' %}");
    expect(block).toContain('{% elsif form_fq_outcome == 120910301 %}');
    expect(block).toContain("{% assign locked_values = '|120910306|' %}");
  });

  it('reads the file quality outcome in the pre-pass without counting it as a finding', () => {
    // It is one of the five codes excluded from the sum AND the answer this rule turns on,
    // so the scan has to come away holding its value while still leaving it out.
    expect(reviewTemplate).toContain("{% assign fq_outcome_codes = '|Q-FQ-01|Q-FQTAX-01|' %}");
    expect(reviewTemplate).toContain(
      '{% if fq_outcome_codes contains scan_code and scan_value %}',
    );
    expect(reviewTemplate).toContain('{% assign form_fq_outcome = scan_value %}');
  });
});

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
