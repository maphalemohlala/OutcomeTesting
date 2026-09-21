import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import optionsTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-answer-options/OT-Answer-Options.webtemplate.source.html?raw';
import { SCALE_OPTIONS } from './checklistForm';

/**
 * The review page re-reads its checklist live, so a question retyped in the Code App shows
 * on the portal at once (project owner, 2026-09-21: "the changes should show immediately
 * after they are made").
 *
 * Power Pages renders from a server-side cache and learns about a Dataverse write through
 * change tracking, which is polled: AD-094 measured up to fifteen minutes and AD-117
 * concluded a longer delay "narrows the window and cannot close it". Change tracking is on
 * for al_question, al_questionversion and al_section; it is not the lever. The lever is the
 * Web API, which is not the Liquid render cache - the same move OT Remediation already makes
 * for its stale case-status badge.
 *
 * These tests pin the shape of that check, and the three things it must never do.
 */

/** The `data-ot-scales` JSON island, with the Liquid branches resolved for one discipline. */
function scales(isTaxReview: boolean): {
  inline: Record<string, { values: number[]; labels: string[] }>;
  grid: Record<string, { labels: string[] }>;
} {
  const start = reviewTemplate.indexOf('<script type="application/json" data-ot-scales>');
  expect(start).toBeGreaterThan(-1);
  const open = reviewTemplate.indexOf('>', start) + 1;
  const end = reviewTemplate.indexOf('</script>', open);
  let json = reviewTemplate.slice(open, end);

  // Resolve `{% if is_tax_review %}…{% else %}…{% endif %}` for the discipline under test.
  json = json.replace(
    /\{%\s*if is_tax_review\s*%\}([\s\S]*?)\{%\s*else\s*%\}([\s\S]*?)\{%\s*endif\s*%\}/g,
    (_match, taxBranch: string, otherBranch: string) => (isTaxReview ? taxBranch : otherBranch),
  );

  return JSON.parse(json);
}

/** The values and labels OT Answer Options assigns for one response type. */
function inlineFromInclude(
  responseType: number,
  isTaxReview: boolean,
): { values: number[]; labels: string[] } {
  const branch = optionsTemplate.slice(optionsTemplate.indexOf(`{% when ${responseType} %}`));
  const next = branch.indexOf('{% when ', 1);
  let body = next === -1 ? branch : branch.slice(0, next);

  body = body.replace(
    /\{%\s*if is_tax\s*%\}([\s\S]*?)\{%\s*else\s*%\}([\s\S]*?)\{%\s*endif\s*%\}/g,
    (_match, taxBranch: string, otherBranch: string) => (isTaxReview ? taxBranch : otherBranch),
  );

  const values = /assign values = '([^']*)'/.exec(body);
  const labels = /assign labels = '([^']*)'/.exec(body);
  expect(values).not.toBeNull();
  expect(labels).not.toBeNull();

  return {
    values: values![1].split(',').map(Number),
    labels: labels![1].split(','),
  };
}

describe('the review page re-reads its checklist through the Web API', () => {
  it('asks Dataverse rather than trusting the render', () => {
    // The Liquid render cache is the thing being worked around, so the read has to leave it.
    expect(reviewTemplate).toContain("'/_api/al_questionversions'");
    expect(reviewTemplate).toContain('$select=al_questionversionid,_al_questionid_value,al_responsetype,al_questiontext');
  });

  it('asks as of the day the server rendered against, not the browser’s day', () => {
    // A client computing its own "today" can disagree with the server across a time zone,
    // which is the class of bug AD-091 was raised to stop.
    expect(reviewTemplate).toContain("var AS_OF = '{{ as_of }}';");
    expect(reviewTemplate).toContain('al_effectiveto gt ');
    expect(reviewTemplate).toContain('al_effectivefrom le ');
  });

  it('runs only on an unsubmitted review', () => {
    // A submitted review is read as of its submission day (BR-013) and must keep rendering
    // the versions it was answered against. Refreshing it would relabel answered history,
    // which is the opposite of what the versioning exists for.
    const start = reviewTemplate.indexOf('data-ot-checklist-stale');
    const guard = reviewTemplate.lastIndexOf('{% unless rv.al_submittedon %}', start);
    expect(guard).toBeGreaterThan(-1);
    expect(reviewTemplate.indexOf('{% endunless %}', reviewTemplate.indexOf('data-ot-scales'))).toBeGreaterThan(start);
  });

  it('carries every question’s question id, which is what it keys on', () => {
    // A version id alone cannot be looked up "which version is in force for this question
    // now", so the row and the fetch both have to carry the question.
    expect(reviewTemplate).toContain('<attribute name="al_questionid" />');
    expect(reviewTemplate).toContain("data-question-id=\"{{ qv['q.al_questionid'] }}\"");
  });

  it('repoints the row at the live version before anything else', () => {
    // The autosave reads data-question-version to decide what it is answering.
    // ResponseGuardPlugin refuses a write against a superseded version, so a row left
    // pointing at the old one would fail the checker's very next tick.
    const apply = reviewTemplate.slice(reviewTemplate.indexOf('function apply(byQuestion)'));
    const repoint = apply.indexOf("row.setAttribute('data-question-version'");
    const rebuild = apply.indexOf('rebuild(row, liveType, current)');
    expect(repoint).toBeGreaterThan(-1);
    expect(repoint).toBeLessThan(rebuild);
  });

  it('starts hidden and survives a page that never runs it', () => {
    // Progressive enhancement: a failure, a refusal or a browser that never runs this
    // leaves the server-rendered form exactly as it was. A stale form is what we already
    // have, so there is nothing to lose by failing quietly.
    expect(reviewTemplate).toMatch(/data-ot-checklist-stale hidden/);
    const script = reviewTemplate.slice(reviewTemplate.indexOf('var notice = document.querySelector'));
    expect(script.slice(0, 600)).toContain('catch (e) { return; }');
    expect(script).toContain('if (request.status < 200 || request.status >= 300) { return; }');
  });

  it('says so when a tick was dropped, and says so when none was', () => {
    // The one consequence a checker has to be told about: an answer whose value the new
    // scale has no option for is gone, and coming back to an empty row with no explanation
    // is how a reviewer concludes their work was lost.
    expect(reviewTemplate).toContain(' been cleared - please answer ');
    expect(reviewTemplate).toContain('had a matching option on the new version');
    expect(reviewTemplate).toContain('Nothing you had already answered was changed.');
  });

  it('re-heads a grid only where every row agrees on the scale', () => {
    // The same test the server makes: a table whose rows disagree has no honest column
    // heading, and leaving the old one is better than inventing one.
    const rehead = reviewTemplate.slice(reviewTemplate.indexOf('function reheadGrids()'));
    expect(rehead).toContain('agreed = false');
    expect(rehead).toContain('if (!agreed || type === null) { continue; }');
    expect(rehead).toContain('heads.length !== scale.labels.length');
  });
});

describe('the emitted scales are not a third copy of the option scales', () => {
  const types = [120910005, 120910006, 120910007, 120910008, 120910009, 120910010];

  it.each([false, true])('matches OT Answer Options exactly (tax review: %s)', (isTax) => {
    const emitted = scales(isTax).inline;

    for (const type of types) {
      expect(emitted[String(type)], `response type ${type}`).toEqual(inlineFromInclude(type, isTax));
    }
  });

  it('covers every scale the include can draw, and no more', () => {
    // A scale the include renders but this map omits would be left stale by the refresh,
    // silently - the row would repoint at the new version and keep the old options.
    const drawn = [...optionsTemplate.matchAll(/\{%\s*when (\d+)\s*%\}/g)].map((m) => Number(m[1]));
    expect(Object.keys(scales(false).inline).map(Number).sort()).toEqual(drawn.sort());
  });

  it('agrees with the Code App on the same values', () => {
    // The app and the portal draw one checklist. Only the values are compared: the document
    // sets an inline scale in upper case and the app's options carry the title-case label.
    for (const type of types) {
      expect(scales(false).inline[String(type)].values, `response type ${type}`).toEqual(
        SCALE_OPTIONS[type].map((option) => option.value),
      );
    }
  });

  it('matches the grid column headings the template picks', () => {
    // Every set of tick-column labels the template can assign has to be one this map
    // emits, and vice versa. Compared as sets rather than by parsing the if-chain: the
    // chain nests a discipline branch inside one of its arms, and a test that has to
    // understand that is a test that breaks when the chain is re-ordered.
    const assigned = [...reviewTemplate.matchAll(/assign glabels = '([^']*)'/g)]
      .map((match) => match[1])
      .sort();

    const emitted = [
      ...Object.values(scales(false).grid).map((scale) => scale.labels.join(',')),
      ...Object.values(scales(true).grid).map((scale) => scale.labels.join(',')),
    ];

    expect([...new Set(emitted)].sort()).toEqual([...new Set(assigned)].sort());
  });
});
