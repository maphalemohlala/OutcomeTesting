import { describe, expect, it } from 'vitest';
import reference from '../../../../docs/reference/checker-checklist.html?raw';
import seedXml from '../../../../data/v8-seed/data.xml?raw';
import { failPoints, formBlocks, inlineOptionsFor, type FailReasonRef } from './checklistForm';
import {
  buildSections,
  type QuestionRef,
  type SectionRef,
  type SectionedAnswer,
  type VersionRef,
} from './reviewSections';

/**
 * The Checker Checklist, checked against the document rather than against a hand-written
 * expectation.
 *
 * `docs/reference/checker-checklist.html` is the build reference the project owner supplied:
 * the form's headings, its section splitting, its column headings and every question, with
 * the repeating rows generated from data at the bottom of the file. `data/v8-seed/data.xml`
 * is what those questions are in Dataverse. This test reads both and asserts the form the
 * app draws off the seed is the form the document draws - so neither the seed nor
 * `formBlocks` can drift from the reference without a red test naming what moved.
 *
 * Three deliberate differences from the reference file, each asserted below so they stay
 * deliberate rather than becoming drift:
 *
 * 1. Remediation and escalation is not a block of this form. The document marks it
 *    "(PICKED UP ON ANOTHER FORM)" and it is built as one - RemediationPage, per case
 *    rather than per review (AD-095), on the project owner's direction of 2026-09-09.
 * 2. The case header opens the form. It is Outcome Case columns captured at intake
 *    (checklist-v8.md, caseHeaderFields), not checklist questions, and the reference file
 *    leaves its place empty rather than transcribing it.
 * 3. Q-TAX-02's middle option reads PASS WITH ISSUES, where the document reads INSUFFICIENT
 *    EVIDENCE (project owner, 2026-09-13; AD-055 amended). The reference file is left as it
 *    was supplied rather than rewritten, so the provenance every other assertion here rests
 *    on is intact and the one option that moved is named in the assertion itself. The same
 *    rewording reaches a grid through optionsFor(scale, isTaxReview), which covers a section
 *    added by checklist administration rather than anything the document draws.
 * 4. Q-TAX-04 "Tax Remedial" ends the Tax check section, and the document does not carry it.
 *    It was added to DEV on 2026-09-19, after the reference was supplied, and back-ported
 *    into the seed on 2026-09-21 (F56) - which is what turned this test red: the seed grew a
 *    question the document has never had. Recorded as a difference rather than drawn into
 *    the reference for the reason (3) gives, and asserted by name and position below so a
 *    SECOND undocumented question cannot hide behind it.
 *
 * Four more from the project owner's direction of 2026-09-24, recorded the same way and for
 * the same reason - the reference stays as supplied, and each change is named where it is
 * asserted:
 *
 * 5. The Suitability subsections are headed by their names alone: "Client Objectives &
 *    Information (COBS 9.2)", where the document reads "E1. Client Objectives ...".
 * 6. Consumer Duty prints no intro line. The document's "Short yes/no judgements only. Record
 *    any detail once in section H." is gone; its section H never existed in V8.
 * 7. The Suitability and CRP grids take an N/A column. Every CRP row offers N/A, and in
 *    Suitability only Q-E4-03 (concessions) does - both on the 120910012 scale.
 * 8. Primary root cause takes several ticks. Its options are the document's, unchanged; the
 *    seed carries a second version of Q-GR-02 on the multi-select type from 2026-09-24, so the
 *    fixture reads the versions in force today rather than every version seeded.
 */

// ---------------------------------------------------------------------------------------
// Reading the reference document
// ---------------------------------------------------------------------------------------

/** The entities and escapes the reference uses, in markup and in its JS string literals. */
function decode(value: string): string {
  return value
    .replace(/\\u([0-9a-fA-F]{4})/g, (_, hex) => String.fromCharCode(parseInt(hex, 16)))
    .replace(/\\"/g, '"')
    .replace(/&ndash;/g, '–')
    .replace(/&nbsp;/g, ' ')
    .replace(/&amp;/g, '&')
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .trim();
}

function matchAll(source: string, pattern: RegExp): string[] {
  return [...source.matchAll(pattern)].map((match) => decode(match[1]));
}

/** The document's headings. */
const documentHeadings = matchAll(reference, /<h2>([\s\S]*?)<\/h2>/g);

/** The intro line under a heading, where the document sets one. */
const documentIntros = matchAll(reference, /<p class="intro">([\s\S]*?)<\/p>/g);

/** Every `.grid` table's heading row: the column heading, then one label per tick column. */
const documentGridHeadings = [
  ...reference.matchAll(/<table class="grid">\s*<thead>([\s\S]*?)<\/thead>/g),
].map((table) => matchAll(table[1], /<th class="(?:label|opt)">([\s\S]*?)<\/th>/g));

/** Every `.meta` table's label cells - the document's inline rows. */
const documentMetaLabels = [...reference.matchAll(/<table class="meta">([\s\S]*?)<\/table>/g)].map(
  (table) => matchAll(table[1], /<td class="lbl">([\s\S]*?)<\/td>/g),
);

/** The option labels beside the boxes of one `.opts` run, by the input's name. */
function documentOptions(name: string): string[] {
  const pattern = new RegExp(`name="${name}" value="[^"]*">([^<]*)</label>`, 'g');
  return matchAll(reference, pattern);
}

interface DocumentRow {
  kind: 'section' | 'lens' | 'row';
  label: string;
  tickbox: boolean;
}

/** One of the reference's `renderGrid` data arrays - the rows it generates a grid from. */
function documentRows(tbodyId: string): DocumentRow[] {
  const body = new RegExp(`renderGrid\\("${tbodyId}", \\[([\\s\\S]*?)\\n\\], `).exec(reference);
  if (!body) throw new Error(`No renderGrid data for ${tbodyId}`);

  return body[1]
    .split('\n')
    .map((line) => line.trim())
    .flatMap((line) => {
      const label = /label: "((?:[^"\\]|\\.)*)"/.exec(line);
      if (!label) return [];
      const kind = /type: "(\w+)"/.exec(line);
      return [
        {
          kind: (kind?.[1] ?? 'row') as DocumentRow['kind'],
          label: decode(label[1]),
          tickbox: line.includes('tickbox: true'),
        },
      ];
    });
}

const documentFailReasons = matchAll(
  /var FAIL_REASONS = \[([\s\S]*?)\n\];/.exec(reference)?.[1] ?? '',
  /^\s*"((?:[^"\\]|\\.)*)",?\s*$/gm,
);

// ---------------------------------------------------------------------------------------
// Reading the seed
// ---------------------------------------------------------------------------------------

type SeedRecord = { id: string; fields: Record<string, string> };

function seedRecords(entity: string): SeedRecord[] {
  const block = new RegExp(`<entity name="${entity}"[\\s\\S]*?</entity>`).exec(seedXml);
  if (!block) throw new Error(`No ${entity} records in the seed`);

  return [...block[0].matchAll(/<record id="([^"]+)">([\s\S]*?)<\/record>/g)].map((record) => ({
    id: record[1],
    fields: Object.fromEntries(
      [...record[2].matchAll(/<field name="([^"]+)"[^>]*?value="([^"]*)"/g)].map((field) => [
        field[1],
        decode(field[2]),
      ]),
    ),
  }));
}

const seedSections: SectionRef[] = seedRecords('al_section').map((record) => ({
  id: record.id,
  code: record.fields.al_sectioncode,
  name: record.fields.al_name,
  order: Number(record.fields.al_displayorder),
  helpText: record.fields.al_helptext ?? null,
}));

const seedQuestions: QuestionRef[] = seedRecords('al_question').map((record) => ({
  id: record.id,
  sectionId: record.fields.al_sectionid,
  order: Number(record.fields.al_displayorder),
  code: record.fields.al_questioncode,
}));

/** In force today: Q-GR-02 has a retired first version and its successor (difference 8). */
const inForceToday = (record: SeedRecord) =>
  !record.fields.al_effectiveto || new Date(record.fields.al_effectiveto) > new Date();

const seedVersions: VersionRef[] = seedRecords('al_questionversion').filter(inForceToday).map((record) => ({
  id: record.id,
  questionId: record.fields.al_questionid,
  order: Number(record.fields.al_displayorder),
  text: record.fields.al_questiontext,
  responseTypeValue: Number(record.fields.al_responsetype),
  responseType: '',
  mandatory: record.fields.al_ismandatory === 'True',
}));

const seedFailReasons: FailReasonRef[] = seedRecords('al_failreason').map((record) => ({
  id: record.id,
  name: record.fields.al_name,
  category: null,
  categoryValue: Number(record.fields.al_category),
  order: Number(record.fields.al_displayorder),
}));

/**
 * The grade the fixture answers, and why it answers one at all.
 *
 * Primary root cause is drawn only when the grade is something other than Pass (AD-149), so
 * a form built from no answers at all cannot show it - and this test is about whether the
 * app draws the document's questions, not about what a blank form looks like. Potential harm
 * is used because it is the grade furthest from Pass, so the conditional row is unambiguously
 * owed. `gradingRules.test.ts` is where the condition itself is tested; here it is only
 * satisfied, so the document comparison has every row to compare.
 */
const GRADE_QUESTION_CODE = 'Q-GR-01';
const POTENTIAL_HARM = 120910304;

interface DocumentAnswer extends SectionedAnswer {
  answerChoice: number | null;
}

/** The AQS form as the app builds it: every section that team owns, in seed order. */
function form(ownerRole: string) {
  const owned = new Set(
    seedRecords('al_section')
      .filter((record) => record.fields.al_ownerrole === ownerRole)
      .map((record) => record.id),
  );

  // Only where this team owns the grading section. Handing the Tax form an AQS answer
  // would put it in the unplaced bucket and add an 'Other answers' block that is not on
  // the document.
  const gradeQuestion = seedRecords('al_question').find(
    (record) => record.fields.al_questioncode === GRADE_QUESTION_CODE,
  );
  const gradeQuestionId =
    gradeQuestion && owned.has(gradeQuestion.fields.al_sectionid) ? gradeQuestion.id : null;
  const gradeVersion = gradeQuestionId
    ? seedVersions.find((version) => version.questionId === gradeQuestionId)
    : undefined;

  const answers: DocumentAnswer[] = gradeVersion
    ? [
        {
          id: 'fixture-grade',
          versionId: gradeVersion.id,
          question: gradeVersion.text,
          responseTypeValue: gradeVersion.responseTypeValue,
          responseType: gradeVersion.responseType,
          answerChoice: POTENTIAL_HARM,
        },
      ]
    : [];

  const sections = buildSections<DocumentAnswer>(
    seedSections.filter((section) => owned.has(section.id)),
    seedQuestions.filter((question) => question.sectionId && owned.has(question.sectionId)),
    seedVersions,
    answers,
  );

  // The Tax form is the one built from Tax-owned sections, so the owner role being
  // filtered on is also the discipline of the review that would be answering it. The
  // inline path reads that flag to decide whether 120910302 is the tax check's "Pass with
  // issues" or the scale's "Insufficient evidence".
  return formBlocks(sections, failPoints(seedFailReasons, new Set()), ownerRole === TAX);
}

const AQS = '120910101';
const TAX = '120910100';

// ---------------------------------------------------------------------------------------

describe('the checklist the app draws matches the reference document', () => {
  it('reads the reference document and the seed', () => {
    expect(documentHeadings.length).toBe(9);
    expect(documentFailReasons.length).toBe(20);
    expect(seedSections.length).toBe(12);

    // 47 in force, not the 46 the document draws: Q-TAX-04 is difference (4) above. Asserted
    // as the document's count plus exactly one, so the number carries its own reason. The
    // seed holds one more - Q-GR-02's retired first version (difference 8).
    expect(seedVersions.length).toBe(46 + 1);
    expect(seedRecords('al_questionversion')).toHaveLength(46 + 1 + 1);
    expect(seedVersions.filter((version) => version.text === 'Tax Remedial')).toHaveLength(1);
  });

  it('opens every block the document heads, in the document’s order', () => {
    // The document's headings less Remediation and escalation, which is its own form
    // (AD-095). Everything before it is a block of this one, in this order.
    const expected = documentHeadings.filter((heading) => heading !== 'Remediation and escalation');

    expect(form(AQS).map((block) => block.title)).toEqual(
      expected.filter((heading) => heading !== 'File Quality - Tax check section'),
    );

    // The Tax team owns the Tax check, the fail points and its own File Quality Outcome.
    expect(form(TAX).map((block) => block.title)).toEqual([
      'File Quality - Tax check section',
      'File Quality – Fail points',
      'File Quality Outcome',
    ]);
  });

  it('heads each grid with the document’s column heading and tick columns', () => {
    const grids = form(AQS).flatMap((block) =>
      block.kind === 'section' && block.layout === 'grid'
        ? [[block.columnHeading, ...block.options.map((option) => option.label)]]
        : [],
    );

    // Difference 7: Suitability and CRP take an N/A column after the document's three.
    const withNa = new Set(['Suitability test point', 'Centralised Retirement Proposition test point']);
    expect(grids).toEqual(
      documentGridHeadings.map((heading) => (withNa.has(heading[0]) ? [...heading, 'N/A'] : heading)),
    );
  });

  it('sets each block’s intro line as the document sets it', () => {
    const intros = form(AQS).flatMap((block) =>
      block.kind === 'section' && block.intro ? [block.intro] : [],
    );

    // Difference 6: Consumer Duty's section H line is not printed.
    expect(intros).toEqual(documentIntros.filter((intro) => !intro.includes('section H')));
  });

  it('splits Suitability core checks into the document’s subsections, rows and outcome lenses', () => {
    const suitability = form(AQS).find((block) => block.title === 'Suitability core checks');
    expect(suitability?.kind).toBe('section');
    if (suitability?.kind !== 'section') return;

    // The document's E1-E5 data, folded into the shape formBlocks produces: a heading, its
    // test points, its outcome lens, and whether the lens row carries a tick box of its own.
    const expected: { heading: string; rows: string[]; lens: string; lensTick: boolean }[] = [];
    for (const row of documentRows('suit-rows')) {
      if (row.kind === 'section') {
        // Difference 5: headed by the name alone, without the document's "E1. " prefix.
        const heading = row.label.replace(/^E\d\. /, '');
        expected.push({ heading, rows: [], lens: '', lensTick: false });
      } else if (row.kind === 'lens') {
        expected[expected.length - 1].lens = row.label;
        expected[expected.length - 1].lensTick = row.tickbox;
      } else {
        expected[expected.length - 1].rows.push(row.label);
      }
    }

    expect(expected).toHaveLength(5);
    expect(
      suitability.groups.map((group) => ({
        heading: group.heading,
        rows: group.rows.map((row) => row.question),
        lens: group.lens,
        lensTick: group.lensTick !== null,
      })),
    ).toEqual(expected);
  });

  it('draws the AML and CRA, CRP and Consumer Duty rows the document lists', () => {
    const rowsOf = (title: string) => {
      const block = form(AQS).find((candidate) => candidate.title === title);
      if (block?.kind !== 'section') throw new Error(`No grid block ${title}`);
      return block.groups.flatMap((group) => group.rows.map((row) => row.question));
    };

    const labels = (tbodyId: string) => documentRows(tbodyId).map((row) => row.label);

    expect(rowsOf('File Quality - AML and CRA checking points')).toEqual(labels('aml-rows'));
    expect(rowsOf('Centralised Retirement Proposition')).toEqual(labels('crp-rows'));
    expect(rowsOf('Consumer Duty overlay')).toEqual(labels('cd-rows'));
  });

  it('labels the inline rows as the document’s .meta tables label them', () => {
    const rowsOf = (blocks: ReturnType<typeof form>, title: string) => {
      const block = blocks.find((candidate) => candidate.title === title);
      if (block?.kind !== 'section') throw new Error(`No block ${title}`);
      return block.groups.flatMap((group) => group.rows.map((row) => row.question));
    };

    // documentMetaLabels, in document order: Tax check, File Quality Outcome, the grade,
    // the two case-note boxes, then the remediation block's judgements.
    // Plus Q-TAX-04 at the end - difference (4). Appended rather than interleaved, so the
    // document's own three rows are still asserted in the document's order.
    expect(rowsOf(form(TAX), 'File Quality - Tax check section')).toEqual([
      ...documentMetaLabels[0],
      'Tax Remedial',
    ]);
    expect(rowsOf(form(TAX), 'File Quality Outcome')).toEqual(documentMetaLabels[1]);
    expect(rowsOf(form(AQS), 'File Quality Outcome')).toEqual(documentMetaLabels[1]);

    // Grading runs the grade, then Primary root cause in its own 3x3 table, then the two
    // note boxes - so the document splits it across .meta tables either side of .rootcause.
    expect(rowsOf(form(AQS), 'Checker judgement and grading')).toEqual([
      ...documentMetaLabels[2],
      decode(/<tr><th colspan="3">([^<]*)<\/th><\/tr>/.exec(reference)?.[1] ?? ''),
      ...documentMetaLabels[3],
    ]);
  });

  it('offers each inline row the document’s own options, in the document’s casing and order', () => {
    const optionsOf = (blocks: ReturnType<typeof form>, title: string, question: string) => {
      const block = blocks.find((candidate) => candidate.title === title);
      if (block?.kind !== 'section') throw new Error(`No block ${title}`);
      const row = block.groups
        .flatMap((group) => group.rows)
        .find((candidate) => candidate.question === question);
      // The discipline comes off the block, which is how ReviewDetailPage reads it too:
      // the tax check's reorder and rename apply to a Tax review and to nothing else.
      return inlineOptionsFor(row?.responseTypeValue ?? null, block.isTaxReview).map(
        (option) => option.label,
      );
    };

    const tax = form(TAX);
    const aqs = form(AQS);

    expect(optionsOf(tax, 'File Quality - Tax check section', 'Tax check reason')).toEqual(
      documentOptions('cc_taxcheckreason'),
    );
    // Deliberate difference 3: the tax check outcome's middle option. The document still
    // reads it INSUFFICIENT EVIDENCE and the form now reads it PASS WITH ISSUES (project
    // owner, 2026-09-13; AD-055 amended). Asserted as a substitution on the document's own
    // list rather than as a literal, so the order, the casing and the other two options are
    // still read from the reference and any further drift still reddens this test.
    expect(optionsOf(tax, 'File Quality - Tax check section', 'Tax check outcome')).toEqual(
      documentOptions('cc_taxcheckoutcome').map((label) =>
        label === 'INSUFFICIENT EVIDENCE' ? 'PASS WITH ISSUES' : label,
      ),
    );
    expect(optionsOf(aqs, 'File Quality Outcome', 'File quality outcome')).toEqual(
      documentOptions('cc_filequalityoutcome'),
    );
    expect(optionsOf(aqs, 'File Quality Outcome', 'Remedial action required?')).toEqual(
      documentOptions('cc_remedialactionrequired'),
    );
    expect(optionsOf(aqs, 'Checker judgement and grading', 'Advice Quality Grade')).toEqual(
      documentOptions('cc_advicequalitygrade'),
    );
    expect(optionsOf(aqs, 'Checker judgement and grading', 'Primary root cause')).toEqual(
      documentOptions('cc_primaryrootcause'),
    );
  });

  it('lists every fail reason the document lists, verbatim and in its order', () => {
    const block = form(AQS).find((candidate) => candidate.kind === 'failpoints');
    expect(block?.kind).toBe('failpoints');
    if (block?.kind !== 'failpoints') return;

    expect(block.title).toBe(documentHeadings[2]);
    expect(block.points.map((point) => point.label)).toEqual(documentFailReasons);

    // Both disciplines pick from the one undivided list (AD-100).
    const tax = form(TAX).find((candidate) => candidate.kind === 'failpoints');
    if (tax?.kind !== 'failpoints') throw new Error('No fail points on the Tax form');
    expect(tax.points.map((point) => point.label)).toEqual(documentFailReasons);
  });
});
