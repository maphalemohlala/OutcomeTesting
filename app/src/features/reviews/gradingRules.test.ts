import { describe, expect, it } from 'vitest';
import {
  GRADE_QUESTION_CODE,
  GRADE_RESPONSE_TYPE,
  ROOT_CAUSE_QUESTION_CODE,
  ROOT_CAUSE_RESPONSE_TYPE,
  rootCauseCleared,
  rootCauseRequired,
  withoutUnaskedRootCause,
} from './gradingRules';
import type { FormRow } from './reviewSections';

/**
 * The client-side mirror of GradingRules. The same cases the C# tests cover, so a change to
 * one that is not made to the other shows up as a disagreement between two suites rather
 * than as a rule that holds on one surface only.
 */

const PASS = 120910300;
const PASS_WITH_ISSUES = 120910303;
const INSUFFICIENT = 120910302;
const POTENTIAL_HARM = 120910304;
const FACTFIND_QUALITY = 120910320;

interface Ticked {
  id: string;
  versionId: string | null;
  question: string;
  responseTypeValue: number | null;
  responseType: string;
  answerChoice: number | null;
}

function row(
  code: string | null,
  responseTypeValue: number,
  answerChoice: number | null = null,
): FormRow<Ticked> {
  return {
    key: `${code ?? 'x'}-key`,
    question: code ?? 'unplaced',
    responseTypeValue,
    responseType: 'x',
    mandatory: true,
    code,
    response:
      answerChoice === null
        ? null
        : {
            id: `${code}-r`,
            versionId: `${code}-v`,
            question: code ?? '',
            responseTypeValue,
            responseType: 'x',
            answerChoice,
          },
  };
}

const grade = (answer: number | null) => row(GRADE_QUESTION_CODE, GRADE_RESPONSE_TYPE, answer);
const rootCause = (answer: number | null = null) =>
  row(ROOT_CAUSE_QUESTION_CODE, ROOT_CAUSE_RESPONSE_TYPE, answer);
const caseNotes = () => row('Q-GR-03', 120910001);

const codes = (rows: FormRow<Ticked>[]) => rows.map((r) => r.code);

describe('rootCauseRequired', () => {
  it('asks for no root cause on a pass', () => {
    // The rule's whole purpose: a passing file has nothing to explain, and Q-GR-02 is
    // seeded mandatory, so before this every Pass review was held at submission by a
    // question whose only honest answer was none of the nine.
    expect(rootCauseRequired(PASS)).toBe(false);
  });

  it.each([PASS_WITH_ISSUES, INSUFFICIENT, POTENTIAL_HARM])('asks for one on %i', (value) => {
    expect(rootCauseRequired(value)).toBe(true);
  });

  it('asks for one on a grade outside the scale', () => {
    // An unrecognised value is not a pass, and asking is the safe direction.
    expect(rootCauseRequired(999)).toBe(true);
  });

  it('asks for none while the grade is unanswered', () => {
    // Not an excuse: the grade is mandatory in its own right. This only keeps Q-GR-02 out
    // of a refusal that cannot yet know whether it is owed.
    expect(rootCauseRequired(null)).toBe(false);
    expect(rootCauseRequired(undefined)).toBe(false);
  });
});

describe('rootCauseCleared', () => {
  it('clears on a pass', () => {
    expect(rootCauseCleared(PASS)).toBe(true);
  });

  it.each([PASS_WITH_ISSUES, INSUFFICIENT, POTENTIAL_HARM, 999])('leaves %i alone', (value) => {
    expect(rootCauseCleared(value)).toBe(false);
  });

  it('clears nothing while the grade is unanswered', () => {
    // The case the two rules are deliberately not each other's negation for: a checker who
    // picks the cause before the grade must not see it wiped on the way past.
    expect(rootCauseCleared(null)).toBe(false);
    expect(rootCauseRequired(null)).toBe(false);
  });
});

describe('withoutUnaskedRootCause', () => {
  it('drops the root cause when the file passed', () => {
    expect(codes(withoutUnaskedRootCause([grade(PASS), rootCause(), caseNotes()]))).toEqual([
      GRADE_QUESTION_CODE,
      'Q-GR-03',
    ]);
  });

  it('keeps it on every other grade', () => {
    expect(
      codes(withoutUnaskedRootCause([grade(POTENTIAL_HARM), rootCause(), caseNotes()])),
    ).toEqual([GRADE_QUESTION_CODE, ROOT_CAUSE_QUESTION_CODE, 'Q-GR-03']);
  });

  it('still draws it while the grade is unanswered', () => {
    // The gate may not demand a cause before it knows the grade, but the page must still
    // show the question: this is the Checker Checklist as the document lays it out, and an
    // ungraded review is owed the row it is about to answer. checklistDocument.test.ts
    // compares what is drawn against the reference document and caught the first attempt,
    // which keyed this on rootCauseRequired and took the row off a blank checklist.
    expect(codes(withoutUnaskedRootCause([grade(null), rootCause(), caseNotes()]))).toEqual([
      GRADE_QUESTION_CODE,
      ROOT_CAUSE_QUESTION_CODE,
      'Q-GR-03',
    ]);
  });

  it('drops a cause already recorded when the grade has since become a pass', () => {
    // The row is gone from the document; the value itself is cleared server-side by
    // ResponseProgressPlugin, because hiding it here would not unsay it in the table.
    expect(
      codes(withoutUnaskedRootCause([grade(PASS), rootCause(FACTFIND_QUALITY), caseNotes()])),
    ).toEqual([GRADE_QUESTION_CODE, 'Q-GR-03']);
  });

  it('leaves a section holding no grade exactly as it was', () => {
    // Every section but Judgement and Grading, and on a Tax review that one is not drawn at
    // all. A grade found in another section would not be this section's.
    const rows = [row('Q-E1-01', 120910006, PASS), row('Q-E1-02', 120910006)];
    expect(withoutUnaskedRootCause(rows)).toBe(rows);
  });

  it('does not mistake a suitability pass for the grade', () => {
    // Pass is also the suitability grid's Pass. The response type is what tells them apart,
    // and without it a ticked test point would hide the root cause on a failing file.
    const rows = [row('Q-E1-01', 120910006, PASS), rootCause(), grade(POTENTIAL_HARM)];
    expect(codes(withoutUnaskedRootCause(rows))).toContain(ROOT_CAUSE_QUESTION_CODE);
  });

  it('reads an unplaced row on its response type alone', () => {
    // An answer whose question row could not be read carries no code, and the grade scale
    // belongs to Q-GR-01 and no other question.
    expect(
      codes(withoutUnaskedRootCause([row(null, GRADE_RESPONSE_TYPE, PASS), rootCause()])),
    ).toEqual([null]);
  });
});
