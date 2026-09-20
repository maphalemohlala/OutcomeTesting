import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';

/**
 * F5, found in DEV on 2026-09-20.
 *
 * Three answers were saved on a Tax review, the page reported "Saved 16:31", and all three
 * were written to al_response correctly. Reloading the page a minute later rendered the
 * form completely empty; the answers only appeared about nine minutes afterwards.
 *
 * The cause is AD-094 and it is not ours to fix: the page reads al_response through Liquid,
 * the write is made by AnswerRequestPlugin as the application user, and a plug-in write
 * never invalidates the portal's data cache. Nothing is lost - the submit check reads the
 * stored rows, so a submit from a page that looks empty still succeeds.
 *
 * What we can fix is that it reads as lost work. A reviewer who reloads and sees an empty
 * form has no way to know their answers are safe, and the obvious response - type it all
 * again - is the worst one.
 *
 * These tests pin the shape of that mitigation, and one thing it must never do.
 */
describe('the review page tells a reviewer when it is behind what they saved', () => {
  it('carries a notice element that starts hidden', () => {
    expect(reviewTemplate).toContain('data-ot-cache-notice');
    expect(reviewTemplate).toMatch(/data-ot-cache-notice hidden/);
  });

  it('remembers what it saved, per review, in this tab only', () => {
    // Per review, so two reviews open in one tab cannot raise each other's notice.
    expect(reviewTemplate).toContain("var CACHE_LAG_KEY = 'ot.savedAnswers.' + REVIEW_ID;");
    expect(reviewTemplate).toContain('window.sessionStorage.setItem(CACHE_LAG_KEY');
  });

  it('survives a browser that refuses storage', () => {
    // Private browsing throws on both the read and the write. The notice is a courtesy and
    // must never break the save that has just succeeded.
    const read = reviewTemplate.slice(reviewTemplate.indexOf('function readSavedAnswers'));
    const write = reviewTemplate.slice(reviewTemplate.indexOf('function writeSavedAnswers'));
    expect(read.slice(0, 400)).toContain('catch (e)');
    expect(write.slice(0, 400)).toContain('catch (e)');
  });

  it('only counts an answer as behind when the page came back empty for it', () => {
    // Never when the page shows something: a value the server did send is never
    // contradicted by what this tab happens to remember.
    expect(reviewTemplate).toContain('if (isBlankNow && isBlankNow())');
    expect(reviewTemplate).toContain('return isBlankRequest(currentRequest());');
  });

  it('forgets an answer that was cleared rather than reporting it as behind', () => {
    expect(reviewTemplate).toContain('if (isBlankRequest(request)) { delete saved[questionVersion]; }');
  });

  it('stops reporting once the window has passed', () => {
    expect(reviewTemplate).toContain('CACHE_LAG_WINDOW_MS');
    expect(reviewTemplate).toContain('if (now - saved[qv] > CACHE_LAG_WINDOW_MS) { continue; }');
  });

  it('says the answers are saved and tells the reviewer not to enter them again', () => {
    expect(reviewTemplate).toContain("' saved. This site caches pages for up to fifteen minutes");
    expect(reviewTemplate).toContain("Do not enter ' + them");
  });

  it('agrees in number, because one answer behind is the common case', () => {
    // It read "One answer you saved is not shown below yet. They are saved... Do not enter
    // them again" on the first live run.
    expect(reviewTemplate).toContain("var they = one ? 'It is' : 'They are';");
    expect(reviewTemplate).toContain("var them = one ? 'it' : 'them';");
    expect(reviewTemplate).toContain("var theyWill = one ? 'it will' : 'they will';");
  });

  /**
   * The one thing this must not do. Re-applying a remembered answer would put a value on
   * screen that the server did not send, indistinguishable from one it did, and a reviewer
   * could not tell which of the two they were reading. The notice is a sentence about the
   * page, never a value in it.
   */
  it('never writes a remembered answer back into the form', () => {
    // Asserted first, so this test cannot pass by the function simply not being there.
    expect(reviewTemplate).toContain('function reportCacheLag');
    const reconcile = reviewTemplate.slice(
      reviewTemplate.indexOf('function reportCacheLag'),
      reviewTemplate.indexOf('function reportCacheLag') + 2000,
    );
    expect(reconcile).not.toMatch(/\.checked\s*=/);
    expect(reconcile).not.toMatch(/\.value\s*=/);
    expect(reconcile).not.toMatch(/innerHTML\s*=\s*saved/);
  });
});
