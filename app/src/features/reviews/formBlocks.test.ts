import { describe, expect, it } from 'vitest';
import { formBlocks, type FailPoint } from './checklistForm';
import type { FormRow, ReviewSection } from './reviewSections';

/**
 * The Checker Checklist lays its sections out under its own headings: E1 to E5 sit together
 * under "Suitability core checks" as one "Suitability test point" table, the fail points
 * are their own block before File Quality Outcome, and each block carries the document's
 * title and intro. The section model has no grouping level, so this is where it is read.
 */
const row = (
  key: string,
  responseTypeValue: number,
  code: string | null = null,
): FormRow => ({
  key,
  question: key,
  responseTypeValue,
  responseType: '',
  mandatory: true,
  code,
  response: null,
});

const section = (
  code: string | null,
  name: string,
  rows: FormRow[],
  helpText: string | null = null,
): ReviewSection => ({ id: `id-${code ?? name}`, code, name, helpText, rows });

const points: FailPoint[] = [{ id: 'fr', label: 'AML - ID verification issue', ticked: false }];

describe('formBlocks', () => {
  const aqs: ReviewSection[] = [
    section('S-AMLCRA', 'AML and CRA checking points', [row('aml1', 120910008)]),
    section('S-FQOUT', 'File Quality: AQS', [row('fq1', 120910005), row('fq2', 120910001)]),
    section('S-E1', 'Client Objectives & Information (COBS 9.2)', [row('e1', 120910006)], 'Lens one'),
    section('S-E2', 'Risk, Capacity & Loss (COBS 9.2 / FG)', [row('e2', 120910006)], 'Lens two'),
    section('S-CRP', 'Centralised Retirement Proposition', [row('crp1', 120910006)]),
    section('S-CD', 'Consumer Duty overlay', [row('cd1', 120910009)], 'Short yes/no judgements only. Record any detail once in section H.'),
    section('S-GRADE', 'Checker judgement and grading', [row('gr1', 120910010)]),
  ];

  it('lays the AQS form out in the document’s blocks, with the fail points before File Quality Outcome', () => {
    expect(formBlocks(aqs, points).map((b) => b.title)).toEqual([
      'File Quality - AML and CRA checking points',
      'File Quality – Fail points',
      'File Quality Outcome',
      'Suitability core checks',
      'Centralised Retirement Proposition',
      'Consumer Duty overlay',
      'Checker judgement and grading',
    ]);
  });

  it('folds E1 to E5 into one Suitability table headed "Suitability test point", one subsection each with its lens', () => {
    const suitability = formBlocks(aqs, points).find((b) => b.id === 'suitability');
    expect(suitability?.kind).toBe('section');
    if (suitability?.kind !== 'section') return;
    expect(suitability.layout).toBe('grid');
    expect(suitability.columnHeading).toBe('Suitability test point');
    expect(suitability.options.map((o) => o.label)).toEqual(['Pass', 'Fail', 'Insufficient evidence']);
    expect(suitability.intro).toContain('consistent Pass/Fail format');
    expect(suitability.groups.map((g) => [g.heading, g.lens, g.rows.map((r) => r.key)])).toEqual([
      ['E1. Client Objectives & Information (COBS 9.2)', 'Lens one', ['e1']],
      ['E2. Risk, Capacity & Loss (COBS 9.2 / FG)', 'Lens two', ['e2']],
    ]);
  });

  it('heads the AML, CRP and Consumer Duty grids as the document does', () => {
    const byId = new Map(formBlocks(aqs, points).map((b) => [b.id, b]));
    const aml = byId.get('amlcra');
    const crp = byId.get('crp');
    const cd = byId.get('cd');
    expect(aml?.kind === 'section' && [aml.columnHeading, aml.options.map((o) => o.label)]).toEqual([
      'Check',
      ['Yes', 'No', 'N/A'],
    ]);
    expect(crp?.kind === 'section' && [crp.columnHeading, crp.intro]).toEqual([
      'Centralised Retirement Proposition test point',
      'Complete this section where retirement income planning or decumulation advice is in scope.',
    ]);
    expect(cd?.kind === 'section' && [cd.columnHeading, cd.intro, cd.options.map((o) => o.label)]).toEqual([
      'Outcome',
      'Short yes/no judgements only. Record any detail once in section H.',
      ['Yes', 'No', 'Insufficient evidence'],
    ]);
  });

  it('gives a block with no subsections a single unheaded group', () => {
    const grade = formBlocks(aqs, points).find((b) => b.id === 'grade');
    expect(grade?.kind === 'section' && grade.groups.map((g) => [g.heading, g.lens])).toEqual([[null, null]]);
  });

  it('lays the Tax form out as Tax check, fail points, File Quality Outcome', () => {
    const tax: ReviewSection[] = [
      section('S-TAX', 'Tax check', [row('t1', 120910004), row('t2', 120910006)]),
      section('S-FQTAX', 'File Quality: Tax', [row('fq1', 120910005)]),
    ];
    expect(formBlocks(tax, points).map((b) => b.title)).toEqual([
      'File Quality - Tax check section',
      'File Quality – Fail points',
      'File Quality Outcome',
    ]);
  });

  it('keeps a section the document does not know under its own name, laid out by its rows', () => {
    const blocks = formBlocks(
      [section('S-NEW', 'A new section', [row('n1', 120910007), row('n2', 120910007)], 'Guidance')],
      points,
    );
    const first = blocks[0];
    expect(first.kind === 'section' && [first.title, first.intro, first.layout, first.options.map((o) => o.label)]).toEqual([
      'A new section',
      'Guidance',
      'grid',
      ['Yes', 'No'],
    ]);
    expect(blocks[blocks.length - 1].id).toBe('failpoints');
  });

  it('keeps the trailing "Other answers" group as its own inline block', () => {
    const blocks = formBlocks([section(null, 'Other answers', [row('o1', 120910000)])], points);
    expect(blocks.map((b) => [b.id, b.title])).toEqual([
      ['id-Other answers', 'Other answers'],
      ['failpoints', 'File Quality – Fail points'],
    ]);
  });

  it('carries the fail points into their block', () => {
    const block = formBlocks([], points).find((b) => b.kind === 'failpoints');
    expect(block?.kind === 'failpoints' && block.points).toEqual(points);
  });
});

describe('the outcome lens tick', () => {
  it("lifts E2's lens tick out of the test points and onto the lens row", () => {
    const blocks = formBlocks(
      [
        section(
          'S-E2',
          'Risk, Capacity & Loss (COBS 9.2 / FG)',
          [
            row('e2-1', 120910006, 'Q-E2-01'),
            row('e2-2', 120910006, 'Q-E2-02'),
            row('e2-lens', 120910007, 'Q-E2-LENS'),
          ],
          'Would a reasonable third party conclude the client was not exposed to foreseeable harm?',
        ),
      ],
      [],
    );
    const block = blocks.find((b) => b.kind === 'section');
    const group = block && block.kind === 'section' ? block.groups[0] : null;

    // It is not a test point, so it does not take a row in the grid.
    expect(group?.rows.map((r) => r.key)).toEqual(['e2-1', 'e2-2']);
    expect(group?.lensTick?.key).toBe('e2-lens');
  });

  it('leaves every other lens row without a tick', () => {
    const blocks = formBlocks(
      [
        section(
          'S-E3',
          'Research & Recommendation Rationale (COBS 9.3)',
          [row('e3-1', 120910006, 'Q-E3-01')],
          'Is the recommendation clearly suitable, not just technically admissible?',
        ),
      ],
      [],
    );
    const block = blocks.find((b) => b.kind === 'section');
    const group = block && block.kind === 'section' ? block.groups[0] : null;
    expect(group?.rows).toHaveLength(1);
    expect(group?.lensTick).toBeNull();
  });
});

