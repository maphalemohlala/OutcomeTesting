import { describe, expect, it } from 'vitest';
import {
  buildSections,
  type QuestionRef,
  type SectionRef,
  type VersionRef,
} from './reviewSections';

/**
 * Question display order is numbered within each section (Q-TAX-01 and Q-E1-01 are both
 * order 1), so a flat sort by question order interleaves the sections. The form lists every
 * question of every section the team owns, in the checklist's own order - which is the
 * order of the Checker Checklist document - with the answer beside it or an empty box.
 */
describe('buildSections', () => {
  const sections: SectionRef[] = [
    { id: 'S-GRADE', code: 'S-GRADE', name: 'Checker judgement and grading', order: 11, helpText: null },
    { id: 'S-TAX', code: 'S-TAX', name: 'Tax check', order: 1, helpText: null },
    {
      id: 'S-E1',
      code: 'S-E1',
      name: 'Client Objectives & Information (COBS 9.2)',
      order: 4,
      helpText: 'Does the evidence support that the advice was built around the client?',
    },
  ];
  const questions: QuestionRef[] = [
    { id: 'Q-TAX-01', sectionId: 'S-TAX', order: 1 },
    { id: 'Q-TAX-02', sectionId: 'S-TAX', order: 2 },
    { id: 'Q-E1-01', sectionId: 'S-E1', order: 1 },
    { id: 'Q-E1-02', sectionId: 'S-E1', order: 2 },
    { id: 'Q-GR-01', sectionId: 'S-GRADE', order: 1 },
  ];
  const version = (id: string, questionId: string, order: number): VersionRef => ({
    id: `${id}-v`,
    questionId,
    order,
    text: id,
    responseTypeValue: 120910006,
    responseType: 'Pass / Fail / Insufficient evidence',
    mandatory: true,
  });
  const versions: VersionRef[] = [
    version('Q-E1-02', 'Q-E1-02', 2),
    version('Q-E1-01', 'Q-E1-01', 1),
    version('Q-TAX-01', 'Q-TAX-01', 1),
    version('Q-TAX-02', 'Q-TAX-02', 2),
    version('Q-GR-01', 'Q-GR-01', 1),
  ];

  const answer = (id: string, versionId: string | null) => ({
    id,
    versionId,
    question: id,
    responseTypeValue: 120910006,
    responseType: 'Pass / Fail / Insufficient evidence',
  });

  it('orders the sections by their display order, not by the order the answers arrived', () => {
    const built = buildSections(sections, questions, versions, [
      answer('r-gr', 'Q-GR-01-v'),
      answer('r-e1', 'Q-E1-01-v'),
      answer('r-tax', 'Q-TAX-01-v'),
    ]);
    expect(built.map((s) => s.id)).toEqual(['S-TAX', 'S-E1', 'S-GRADE']);
  });

  it('lists every question in force in a section, answered or not, in question order', () => {
    const built = buildSections(sections, questions, versions, [answer('r-2', 'Q-E1-02-v')]);
    const e1 = built.find((s) => s.id === 'S-E1');
    expect(e1?.rows.map((r) => [r.question, r.response?.id ?? null])).toEqual([
      ['Q-E1-01', null],
      ['Q-E1-02', 'r-2'],
    ]);
  });

  it('shows a section the team owns even when nothing in it has been answered', () => {
    const built = buildSections(sections, questions, versions, []);
    expect(built.map((s) => s.id)).toEqual(['S-TAX', 'S-E1', 'S-GRADE']);
  });

  it('drops a section with no question in force, since the form has no row to show for it', () => {
    const built = buildSections(
      [...sections, { id: 'S-EMPTY', code: 'S-EMPTY', name: 'Empty', order: 2, helpText: null }],
      questions,
      versions,
      [],
    );
    expect(built.map((s) => s.id)).not.toContain('S-EMPTY');
  });

  it('carries the section code, name and help text so the page can place and caption it', () => {
    const [, e1] = buildSections(sections, questions, versions, []);
    expect(e1.code).toBe('S-E1');
    expect(e1.name).toBe('Client Objectives & Information (COBS 9.2)');
    expect(e1.helpText).toBe(
      'Does the evidence support that the advice was built around the client?',
    );
  });

  it('carries the version wording, type and mandatory flag onto the row', () => {
    const [tax] = buildSections(sections, questions, versions, []);
    expect(tax.rows[0]).toMatchObject({
      key: 'Q-TAX-01-v',
      question: 'Q-TAX-01',
      responseTypeValue: 120910006,
      mandatory: true,
    });
  });

  it('keeps an answer whose version is not on this form in a trailing group rather than hiding it', () => {
    const built = buildSections(sections, questions, versions, [
      answer('r-orphan', 'Q-UNKNOWN-v'),
      answer('r-none', null),
      answer('r-tax', 'Q-TAX-01-v'),
    ]);
    const other = built[built.length - 1];
    expect(other.id).toBe('other');
    expect(other.name).toBe('Other answers');
    expect(other.rows.map((r) => r.response?.id)).toEqual(['r-orphan', 'r-none']);
  });

  it('adds no trailing group when every answer found its row', () => {
    const built = buildSections(sections, questions, versions, [answer('r-tax', 'Q-TAX-01-v')]);
    expect(built.map((s) => s.id)).not.toContain('other');
  });

  it('breaks a display-order tie by section name so the order is stable', () => {
    const tied: SectionRef[] = [
      { id: 'b', code: null, name: 'Beta', order: 2, helpText: null },
      { id: 'a', code: null, name: 'Alpha', order: 2, helpText: null },
    ];
    const built = buildSections(
      tied,
      [
        { id: 'q-b', sectionId: 'b', order: 1 },
        { id: 'q-a', sectionId: 'a', order: 1 },
      ],
      [version('q-b', 'q-b', 1), version('q-a', 'q-a', 1)],
      [],
    );
    expect(built.map((s) => s.id)).toEqual(['a', 'b']);
  });

  it('falls back to the question order when the version carries none', () => {
    const built = buildSections(
      [sections[1]],
      questions,
      [version('Q-TAX-02', 'Q-TAX-02', 0), version('Q-TAX-01', 'Q-TAX-01', 0)],
      [],
    );
    expect(built[0].rows.map((r) => r.question)).toEqual(['Q-TAX-01', 'Q-TAX-02']);
  });
});
