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
 * Two deliberate differences from the reference file, both asserted below so they stay
 * deliberate rather than becoming drift:
 *
 * 1. Remediation and escalation is not a block of this form. The document marks it
 *    "(PICKED UP ON ANOTHER FORM)" and it is built as one - RemediationPage, per case
 *    rather than per review (AD-095), on the project owner's direction of 2026-09-09.
 * 2. The case header opens the form. It is Outcome Case columns captured at intake
 *    (checklist-v8.md, caseHeaderFields), not checklist questions, and the reference file
 *    leaves its place empty rather than transcribing it.
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

const seedVersions: VersionRef[] = seedRecords('al_questionversion').map((record) => ({
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

/** The AQS form as the app builds it: every section that team owns, in seed order. */
function form(ownerRole: string) {
  const owned = new Set(
    seedRecords('al_section')
      .filter((record) => record.fields.al_ownerrole === ownerRole)
      .map((record) => record.id),
  );

  const sections = buildSections<SectionedAnswer>(
    seedSections.filter((section) => owned.has(section.id)),
    seedQuestions.filter((question) => question.sectionId && owned.has(question.sectionId)),
    seedVersions,
    [],
  );

  return formBlocks(sections, failPoints(seedFailReasons, new Set()));
}

const AQS = '120910101';
const TAX = '120910100';

// ---------------------------------------------------------------------------------------

describe('the checklist the app draws matches the reference document', () => {
  it('reads the reference document and the seed', () => {
    expect(documentHeadings.length).toBe(9);
    expect(documentFailReasons.length).toBe(20);
    expect(seedSections.length).toBe(12);
    expect(seedVersions.length).toBe(46);
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

    expect(grids).toEqual(documentGridHeadings);
  });

  it('sets each block’s intro line as the document sets it', () => {
    const intros = form(AQS).flatMap((block) =>
      block.kind === 'section' && block.intro ? [block.intro] : [],
    );

    expect(intros).toEqual(documentIntros);
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
        expected.push({ heading: row.label, rows: [], lens: '', lensTick: false });
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
    expect(rowsOf(form(TAX), 'File Quality - Tax check section')).toEqual(documentMetaLabels[0]);
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
      return inlineOptionsFor(row?.responseTypeValue ?? null).map((option) => option.label);
    };

    const tax = form(TAX);
    const aqs = form(AQS);

    expect(optionsOf(tax, 'File Quality - Tax check section', 'Tax check reason')).toEqual(
      documentOptions('cc_taxcheckreason'),
    );
    expect(optionsOf(tax, 'File Quality - Tax check section', 'Tax check outcome')).toEqual(
      documentOptions('cc_taxcheckoutcome'),
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
