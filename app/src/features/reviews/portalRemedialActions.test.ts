import { describe, expect, it } from 'vitest';
import reviewTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-review-detail/OT-Review-Detail.webtemplate.source.html?raw';
import layoutTemplate from '../../../../powerpages/outcome-testing---outcometesting/web-templates/ot-layout/OT-Layout.webtemplate.source.html?raw';
import portalCssRaw from '../../../../powerpages/outcome-testing---outcometesting/web-files/outcome-testing.css?raw';

/**
 * The checker writes a remedial action for every fail point before submitting (project
 * owner, 2026-09-29). The server's list (Remediation.NonPassItems) is the one that counts;
 * the page mirrors it so the checker sees the rows as they tick, and a disagreement surfaces
 * as a refused submit naming the item. The mirror is the part most likely to drift, so it is
 * executed here rather than only read.
 */

const template = reviewTemplate.replace(/\r\n/g, '\n');

type Answer = { code: string; type: string; text: string; value: string };
type Items = (answers: Answer[], failPoints: string[], isTax: boolean) => { owed: boolean; items: string[] };

function itemsFunction(): Items {
  const start = template.indexOf('/* ot-remedial-items:start */');
  const end = template.indexOf('/* ot-remedial-items:end */');
  expect(start).toBeGreaterThan(-1);
  expect(end).toBeGreaterThan(start);
  const source = template.slice(start, end);
  return new Function(`${source}; return otRemedialItems;`)() as Items;
}

const PASS_FAIL_INSUFFICIENT = '120910006';
const YES_NO_NA = '120910008';
const YES_NO_INSUFFICIENT = '120910009';
const YES_NO = '120910007';
const OUTCOME_SCALE = '120910014';

const answer = (code: string, type: string, text: string, value: string): Answer => ({ code, type, text, value });

describe('the fail points the page lists', () => {
  const items = itemsFunction();

  it('lists each non-pass answer as "question: answer", then each ticked fail point', () => {
    const result = items(
      [
        answer('Q-E1-01', PASS_FAIL_INSUFFICIENT, ' Client objectives clearly evidenced ', '120910301'),
        answer('Q-E1-02', PASS_FAIL_INSUFFICIENT, 'Risk profile', '120910300'),
        answer('Q-AML-01', YES_NO_NA, 'ID verification completed', '120910306'),
        answer('Q-CD-01', YES_NO_INSUFFICIENT, 'Products and services', '120910302'),
        answer('Q-GR-01', '120910010', 'Advice Quality Grade', '120910303'),
      ],
      [' AML - No CRA completed or missing data fields '],
      false,
    );

    expect(result.items).toEqual([
      'Client objectives clearly evidenced: Fail',
      'ID verification completed: No',
      'Products and services: Insufficient evidence',
      'AML - No CRA completed or missing data fields',
    ]);
  });

  it('leaves out the outcome questions, N/A, Yes and plain Yes / No', () => {
    const result = items(
      [
        answer('Q-FQ-01', PASS_FAIL_INSUFFICIENT, 'File quality outcome', '120910301'),
        answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910301'),
        answer('Q-AML-02', YES_NO_NA, 'CRA on file', '120910307'),
        answer('Q-CD-02', YES_NO_INSUFFICIENT, 'Price and value', '120910305'),
        answer('Q-FQ-03', YES_NO, 'Remedial action required?', '120910306'),
      ],
      [],
      false,
    );

    expect(result.items).toEqual([]);
  });

  it('lists a Consumer Duty outcome below Pass under its own grade, and leaves a Pass out', () => {
    const result = items(
      [
        answer('Q-CD-01', OUTCOME_SCALE, 'Products and services', '120910303'),
        answer('Q-CD-02', OUTCOME_SCALE, 'Price and value', '120910302'),
        answer('Q-CD-03', OUTCOME_SCALE, 'Consumer understanding', '120910304'),
        answer('Q-CD-04', OUTCOME_SCALE, 'Consumer support', '120910300'),
      ],
      [],
      false,
    );

    expect(result.items).toEqual([
      'Products and services: Pass with issues',
      'Price and value: Insufficient evidence',
      'Consumer understanding: Potential harm',
    ]);
    // An item alone does not make remediation owed; the grade and the flag decide that.
    expect(result.owed).toBe(false);
  });

  it('keeps the characters Liquid escapes, so the key matches the server', () => {
    const result = items([answer('Q-X', PASS_FAIL_INSUFFICIENT, 'Fees & "charges" <agreed>', '120910301')], [], false);
    expect(result.items).toEqual(['Fees & "charges" <agreed>: Fail']);
  });

  it('says an AQS check owes remediation on a non-pass grade or a Yes to Remedial action required?', () => {
    expect(items([answer('Q-GR-01', '120910010', 'Grade', '120910303')], [], false).owed).toBe(true);
    expect(items([answer('Q-FQ-03', YES_NO, 'Remedial action required?', '120910305')], [], false).owed).toBe(true);
    expect(items([answer('Q-GR-01', '120910010', 'Grade', '120910300')], [], false).owed).toBe(false);
  });

  it('says a Tax check owes remediation on a Fail or Insufficient outcome or a Yes to its flag', () => {
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910301')], [], true).owed).toBe(true);
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910302')], [], true).owed).toBe(true);
    expect(items([answer('Q-FQTAX-03', YES_NO, 'Remedial action required?', '120910305')], [], true).owed).toBe(true);
    expect(items([answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910300')], [], true).owed).toBe(false);
  });

  it('gives each Tax category answered Yes its own row, after the fail points, as the server lists them', () => {
    const result = items(
      [
        answer('Q-TAX-02', PASS_FAIL_INSUFFICIENT, 'Tax check outcome', '120910301'),
        answer('Q-TAX-11', YES_NO, 'Other: remediation required?', '120910305'),
        answer('Q-TAX-05', YES_NO, 'Calculations: remediation required?', '120910306'),
        answer('Q-TAX-07', YES_NO, 'Wording: remediation required?', '120910305'),
        // Retired at UAT (2026-11-25): a Yes left on Omissions is no longer a row.
        answer('Q-TAX-09', YES_NO, 'Omissions: remediation required?', '120910305'),
      ],
      ['No OEL'],
      true,
    );

    expect(result.owed).toBe(true);
    expect(result.items).toEqual(['No OEL', 'Wording', 'Other']);
  });

  it('shows the card whenever remediation is owed, categories being rows of it', () => {
    expect(template).toContain('root.hidden = !result.owed;');
  });

  it('draws File Quality Outcome hidden on a Tax check an AQS check follows, so a submitted one shows no empty heading', () => {
    expect(template).toContain("{% if blk == 'S-FQ' and tax_fq_excused %}{% assign blk_hidden = ' hidden' %}{% endif %}");
    expect(template).toContain('<h2{{ blk_hidden }}>{{ blk_title | escape }}');
    expect(template).toContain('<table class="meta"{{ blk_hidden }}>');
    // The script still shows it again when the checker returns the case as Tax only.
    expect(template).toContain('table.hidden = !alone;');
  });
});

describe('the outcome rules on the page', () => {
  const start = template.indexOf('/* ot-remedial-items:start */');
  const end = template.indexOf('/* ot-remedial-items:end */');
  const grading = new Function(`${template.slice(start, end)}; return otGrading;`)() as (
    answers: { code: string; type: string; text: string; value: string }[],
    failPoints: string[],
  ) => { fileQuality: string | null; grades: string[] | null; remedial: boolean };
  const point = (value: string) => ({ code: 'Q-E1-01', type: '120910012', text: 'A point', value });
  const aml = (value: string) => ({ code: 'Q-AML-01', type: '120910008', text: 'ID', value });
  const grade = (value: string) => ({ code: 'Q-GR-01', type: '120910010', text: 'Grade', value });

  it('does not let a Consumer Duty outcome restrict the grade (record-only, 2026-11-24)', () => {
    const cd = (value: string) => ({ code: 'Q-CD-01', type: OUTCOME_SCALE, text: 'Products', value });
    expect(grading([cd('120910304'), cd('120910303')], []).grades).toBeNull();
    expect(grading([aml('120910305'), point('120910300'), cd('120910304')], []).grades).toEqual(['120910300']);
  });

  it('reads all Pass or N/A as a Pass with nothing to put right', () => {
    expect(grading([aml('120910305'), point('120910300'), point('120910307')], [])).toEqual({
      fileQuality: '120910300',
      grades: ['120910300'],
      remedial: false,
    });
  });

  it('reads Pass with issues as that grade with a remedial action', () => {
    const rules = grading([aml('120910305'), point('120910303')], []);
    expect(rules.grades).toEqual(['120910303']);
    expect(rules.remedial).toBe(true);
  });

  it('allows only Insufficient evidence or Potential harm once either is recorded', () => {
    const rules = grading([point('120910303'), point('120910301'), grade('120910304')], []);
    expect(rules.grades).toEqual(['120910302', '120910304']);
    expect(rules.remedial).toBe(true);
  });

  it('fails File Quality on a No or a ticked fail point', () => {
    expect(grading([aml('120910306')], []).fileQuality).toBe('120910301');
    expect(grading([aml('120910305')], ['No OEL on file']).fileQuality).toBe('120910301');
  });
});

describe('the card on the page', () => {
  it('sits after "Who carries this fail" and before the submit, hidden until remediation is owed', () => {
    const accountability = template.indexOf('data-ot-accountability\n');
    const card = template.indexOf('data-ot-remedial\n');
    const submit = template.indexOf('data-ot-submit\n');
    expect(accountability).toBeGreaterThan(-1);
    expect(card).toBeGreaterThan(accountability);
    expect(submit).toBeGreaterThan(card);
    expect(template).toContain('<h2 class="ot-card__title" id="ot-remedial-heading">Fail points and remedial actions</h2>');
  });

  it('lays its rows out like the other tables, with the numbers clear of the card edge', () => {
    // Unstyled, the table fell back to the browser's 1px cell padding, sat hard against the
    // card's left border and stopped short of its right one (project owner, 2026-09-30).
    // ot-table is the house table: full width, padded cells, a rule under each row.
    expect(template).toMatch(/<section class="ot-card ot-remedial"[\s\S]*?<table class="ot-table ot-remedial__table">/);
    const css = portalCssRaw.replace(/\r\n/g, '\n');
    expect(css).toMatch(/\.ot-remedial__table th:first-child,\n\.ot-remedial__table td:first-child \{[^}]*white-space: nowrap;/);
  });

  it('moved the stylesheet version with the CSS change', () => {
    const version = /outcome-testing\.css\?v=(\d+)/.exec(layoutTemplate);
    expect(Number(version![1])).toBeGreaterThanOrEqual(25);
  });

  it('draws what the checker already parked, from the review', () => {
    expect(template).toContain('<attribute name="al_pendingremedialactions" />');
    expect(template).toContain('data-ot-remedial-saved="{{ rv.al_pendingremedialactions | escape }}"');
  });

  it('gives every answer row its question text, which is half of each item', () => {
    const roots = template.match(/<tr data-ot-answer\n[\s\S]*?>/g) ?? [];
    expect(roots.length).toBe(2);
    for (const root of roots) {
      expect(root).toContain('data-question-text="{{ qv.al_questiontext | escape }}"');
    }
  });

  it('keeps a row\'s words when its fail point is unticked, so ticking it again brings them back', () => {
    // Rows are redrawn from the map on every change; nothing removes an entry from it.
    expect(template).toContain("area.innerHTML = otRemedialMarkup(texts[key] || '');");
    expect(template).not.toContain('delete texts[');
  });

  it('saves through the checker\'s own contact row', () => {
    expect(template).toContain('al_remedialactionsrequest: JSON.stringify({ reviewId: reviewId, actions: entries })');
  });

  it('holds the submit until every row has words and the last save has landed', () => {
    const click = template.indexOf("button.addEventListener('click'");
    const missing = template.indexOf('window.otRemedial.missing()', click);
    const flush = template.indexOf('window.otRemedial.flush(function (ok)', click);
    const header = template.indexOf('window.otHeader.flush', click);
    expect(missing).toBeGreaterThan(click);
    expect(flush).toBeGreaterThan(missing);
    expect(header).toBeGreaterThan(flush);
  });
});

/** The card's own script, from its first line to the closing tag. */
function cardScript(): string {
  const start = template.indexOf("var root = document.querySelector('[data-ot-remedial]');");
  expect(start).toBeGreaterThan(-1);
  const end = template.indexOf('</script>', start);
  return template.slice(start, end);
}

/** A named function's source out of a script, up to the next line at its own indent closing it. */
function fn(script: string, signature: string): string {
  const start = script.indexOf(signature);
  expect(start).toBeGreaterThan(-1);
  const lineStart = script.lastIndexOf('\n', start) + 1;
  const indent = script.slice(lineStart, start);
  const end = script.indexOf(`\n${indent}}`, start);
  expect(end).toBeGreaterThan(start);
  return script.slice(start, end + indent.length + 2);
}

/**
 * A question retired and succeeded while the review is open (I3, 2026-09-29). The freshness
 * check moves the row onto the successor's version and wording; the server keys the item by
 * the successor's text, so the card must too, or the submit is refused naming a row the card
 * does not show.
 */
describe('the freshness check keeps the card in step', () => {
  const apply = fn(template, 'function apply(byQuestion) {');

  it('re-keys the row by the successor\'s wording', () => {
    expect(apply).toContain("row.setAttribute('data-question-text', current.al_questiontext || '');");
  });

  it('redraws the card once rows have moved, if the card is on the page', () => {
    const moved = apply.indexOf('if (!moved.length) { return; }');
    const redraw = apply.indexOf('window.otRemedial.redraw();');
    expect(moved).toBeGreaterThan(-1);
    expect(redraw).toBeGreaterThan(moved);
    expect(apply).toContain("if (window.otRemedial && typeof window.otRemedial.redraw === 'function') { window.otRemedial.redraw(); }");
  });

  it('is offered a redraw by the card', () => {
    expect(cardScript()).toMatch(/window\.otRemedial = \{[\s\S]*?redraw: draw,/);
  });
});

/**
 * CACHE_LAG on the card (I4, 2026-09-29). The save goes through a plug-in that writes the
 * review, so a reload inside AD-094's fifteen minutes can draw an older map - and the next
 * save would send that stale map over the newer one, which a submit then stamps into the
 * write-once column. The page's stance, as for the header, answers and accountability: the
 * tab remembers what it saved and warns; it never re-applies it.
 */
describe('the card after a reload inside the cache window', () => {
  const card = cardScript();

  it('remembers what this tab last saved, per review, for twenty minutes', () => {
    expect(card).toContain("var REMEDIAL_LAG_KEY = 'ot.savedRemedial.' + reviewId;");
    expect(card).toContain('var REMEDIAL_LAG_WINDOW_MS = 20 * 60 * 1000;');
    expect(card).toMatch(/writeSavedRemedial\(\{ at: Date\.now\(\), map: canonical\(sent\) \}\)/);
  });

  it('never lets storage stop the card', () => {
    expect(fn(card, 'function readSavedRemedial() {')).toMatch(/try \{[\s\S]*window\.sessionStorage\.getItem\(REMEDIAL_LAG_KEY\)[\s\S]*catch \(e\)/);
    expect(fn(card, 'function writeSavedRemedial(')).toMatch(/try \{[\s\S]*window\.sessionStorage\.setItem\(REMEDIAL_LAG_KEY[\s\S]*catch \(e\)/);
  });

  it('warns after drawing when the drawn map differs, and forgets once it matches or the window passes', () => {
    const report = fn(card, 'function reportRemedialLag() {');
    expect(report).toContain('Date.now() - remembered.at > REMEDIAL_LAG_WINDOW_MS || remembered.map === rendered');
    expect(report).toContain('writeSavedRemedial(null);');
    expect(report).toContain('Your remedial actions are not shown here yet. Your last change was saved.');
    expect(report).toContain('reload in a few minutes');
    expect(card).toMatch(/\n\s*draw\(\);\n\s*reportRemedialLag\(\);/);
  });

  it('does not re-apply the remembered map', () => {
    const report = fn(card, 'function reportRemedialLag() {');
    expect(report).not.toMatch(/texts\[/);
    expect(report).not.toContain('save(');
  });

  it('compares maps as the server stores them: trimmed, blanks dropped, in key order', () => {
    const start = template.indexOf('/* ot-remedial-canonical:start */');
    const end = template.indexOf('/* ot-remedial-canonical:end */');
    expect(start).toBeGreaterThan(-1);
    expect(end).toBeGreaterThan(start);
    const canonical = new Function(`${template.slice(start, end)}; return canonical;`)() as (
      entries: unknown,
    ) => string;

    expect(canonical([{ item: 'B', text: ' two ' }, { item: 'A', text: 'one' }, { item: 'C', text: '  ' }])).toBe(
      canonical([{ item: 'A', text: 'one' }, { item: 'B', text: 'two' }]),
    );
    expect(canonical([{ item: 'A', text: 'one' }])).not.toBe(canonical([{ item: 'A', text: 'changed' }]));
    expect(canonical(null)).toBe(canonical([]));
    // The server rebuilds formatted markup; the same words in other tags are the same map.
    expect(canonical([{ item: 'A', text: '<b>one</b> two' }])).toBe(canonical([{ item: 'A', text: '<p><strong>one</strong> two</p>' }]));
  });

  it('opens words written before the editor as escaped text, and formatted words as they were stored', () => {
    const start = template.indexOf('/* ot-remedial-canonical:start */');
    const end = template.indexOf('/* ot-remedial-canonical:end */');
    const markup = new Function(`${template.slice(start, end)}; return otRemedialMarkup;`)() as (text: unknown) => string;

    expect(markup('Line one\nA <script> & more')).toBe('<p>Line one<br>A &lt;script&gt; &amp; more</p>');
    expect(markup('<p><strong>Bold</strong></p>')).toBe('<p><strong>Bold</strong></p>');
    expect(markup('   ')).toBe('');
    expect(markup(null)).toBe('');
  });

  it('stores what the editor holds as markup that starts with a tag, so it is never read back as plain text', () => {
    // TEST 2026-11-23: a first line typed before any Enter has no <p>, so "Recalculate&nbsp;"
    // was stored, read back as plain text and shown with a literal "&nbsp;".
    const start = template.indexOf('/* ot-remedial-canonical:start */');
    const end = template.indexOf('/* ot-remedial-canonical:end */');
    const stored = new Function(`${template.slice(start, end)}; return otRemedialStored;`)() as (html: string, words: string) => string;

    expect(stored('Recalculate&nbsp;', 'Recalculate ')).toBe('<p>Recalculate&nbsp;</p>');
    expect(stored('Recalculate <b>the CGT</b><p>next</p><ul><li>one</li></ul>', 'x')).toBe(
      '<p>Recalculate <b>the CGT</b></p><p>next</p><ul><li>one</li></ul>',
    );
    expect(stored('<b>Bold</b> start', 'Bold start')).toBe('<p><b>Bold</b> start</p>');
    expect(stored('<p>Already</p>', 'Already')).toBe('<p>Already</p>');
    expect(stored('<ul><li>one</li></ul>', 'one')).toBe('<ul><li>one</li></ul>');
    expect(stored('<br>', '  ')).toBe('');
  });

  it('saves through that function', () => {
    expect(cardScript()).toContain("otRemedialStored(area.innerHTML, area.textContent || '')");
  });

  it('gives each remedial action the formatting editor, counted against the column', () => {
    const card = cardScript();
    expect(card).toContain("area.setAttribute('data-ot-maxlength', '4000');");
    expect(card).toContain("['bold', 'Bold', '<strong>B</strong>']");
    expect(card).toContain("['underline', 'Underline', '<u>U</u>']");
    expect(card).toContain("['insertUnorderedList', 'Bulleted list', '&bull; List']");
    expect(card).toContain("document.execCommand('defaultParagraphSeparator', false, 'p');");
    expect(card).not.toContain("createElement('textarea')");
    expect(layoutTemplate).toContain("document.querySelectorAll('textarea[maxlength], [data-ot-maxlength]')");
  });
});

/** A submitted review locks the card with the questionnaire (M1, 2026-09-29). */
describe('the card once the review is submitted', () => {
  it('is locked by lockPage', () => {
    const lock = fn(template, 'function lockPage() {');
    expect(lock).toContain("if (window.otRemedial && typeof window.otRemedial.lock === 'function') { window.otRemedial.lock(); }");
  });

  it('disables its boxes, cancels a pending save and keeps a redraw disabled', () => {
    const card = cardScript();
    const lock = fn(card, 'function lock() {');
    expect(lock).toContain('window.clearTimeout(timer);');
    expect(lock).toContain('locked = true;');
    expect(lock).toMatch(/boxes\[b\]\.disabled = true;/);
    expect(card).toContain("area.contentEditable = locked ? 'false' : 'true';");
    expect(lock).toContain("editors[e].contentEditable = 'false';");
    expect(card).toMatch(/window\.otRemedial = \{[\s\S]*?lock: lock,/);
  });
});
