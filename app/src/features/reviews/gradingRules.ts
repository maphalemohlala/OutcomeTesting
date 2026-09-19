/**
 * The AQS Judgement and Grading section's conditional primary root cause (item 3,
 * 2026-09-19).
 *
 * The client-side mirror of `GradingRules` in the plug-in assembly, which is the authority:
 * `SubmitReviewPlugin` decides there whether the root cause is owed, and
 * `ResponseProgressPlugin` clears a recorded one there when the grade returns to Pass, so
 * neither this page nor the portal can hold a different rule. This exists so the document
 * does not draw a question the review no longer owes (AD-041).
 *
 * When one changes, change all three - here, `GradingRules.cs`, and `OT Review Detail`.
 */

import type { FormRow, SectionedAnswer } from './reviewSections';

/**
 * Advice Quality Grade and Primary root cause. Both are read by name in compiled C# and
 * both are protected from retirement by `ChecklistGuards` (AD-122).
 */
export const GRADE_QUESTION_CODE = 'Q-GR-01';
export const ROOT_CAUSE_QUESTION_CODE = 'Q-GR-02';

/**
 * The grade's own four-value scale and the nine-cause single select. Each belongs to exactly
 * one question in the V8 checklist, which is what AD-055 deliberately arranged, so they are
 * a sound way to find the two rows on a page that has the response type in hand and may not
 * have the code.
 */
export const GRADE_RESPONSE_TYPE = 120910010;
export const ROOT_CAUSE_RESPONSE_TYPE = 120910003;

/** Pass, as `ResponseRules.ChoicePass` has it. */
const PASS = 120910300;

/**
 * Whether the review still owes a primary root cause.
 *
 * Owed for every grade but Pass, including a grade outside the scale - an unrecognised value
 * is not a pass, and asking for the cause is the safe direction.
 *
 * Not owed while the grade is unanswered. The grade is mandatory in its own right, so an
 * ungraded review is refused at submission anyway; until it is given, whether the root cause
 * is owed is simply not knowable.
 */
export function rootCauseRequired(gradeAnswer: number | null | undefined): boolean {
  return gradeAnswer != null && gradeAnswer !== PASS;
}

/**
 * Whether a recorded root cause must be let go, because the grade now says the file passed.
 *
 * Deliberately not the negation of `rootCauseRequired`: an unanswered grade owes no root
 * cause and destroys none either, so a checker who picks the cause before the grade does not
 * have it wiped on the way past. The clearing itself happens server-side; this is here so
 * both copies of the rule read the same way.
 */
export function rootCauseCleared(gradeAnswer: number | null | undefined): boolean {
  return gradeAnswer != null && gradeAnswer === PASS;
}

/** The answer recorded against a row, where the row's answer type carries one. */
function choiceOn(row: FormRow<SectionedAnswer>): number | null {
  /*
   * Read structurally rather than through `TickedAnswer`. `formBlocks` is generic over the
   * answer shape and the grade is a ticked answer whatever else the caller holds, so a cast
   * here is what lets the rule apply without narrowing every caller to one answer type.
   */
  const response = row.response as { answerChoice?: number | null } | null;
  return response?.answerChoice ?? null;
}

/** Whether the row is a question, matched on its response type and confirmed by its code. */
function isQuestion(
  row: FormRow<SectionedAnswer>,
  responseType: number,
  code: string,
): boolean {
  if (row.responseTypeValue !== responseType) return false;
  // An unplaced answer has no code to confirm against (`reviewSections`), and the response
  // type alone is enough there: it belongs to this question and no other.
  return row.code == null || row.code.trim().toUpperCase() === code;
}

/**
 * The rows to draw, with the primary root cause taken out unless this review owes one.
 *
 * Keyed on `rootCauseRequired`, so an **ungraded** review does not draw it either. The
 * batch's wording is "visible and required only when the Advice quality grade is anything
 * other than Pass", and nothing is not anything: the question appears when the grade
 * arrives, and only if that grade is not a Pass.
 *
 * `checklistDocument.test.ts` compares what is drawn here against the reference Checker
 * Checklist, and its fixture answers the grade for exactly this reason - a blank form has no
 * grade, so a conditional row cannot be read off one.
 *
 * Applied to a section's own rows, because the grade and the cause sit in the same section
 * and a grade found anywhere else would not be this section's. A section holding no grade
 * row is returned untouched - every section but Judgement and Grading, and on a Tax review
 * that one is not drawn at all.
 */
export function withoutUnaskedRootCause<T extends SectionedAnswer>(
  rows: FormRow<T>[],
): FormRow<T>[] {
  const grade = rows.find((row) => isQuestion(row, GRADE_RESPONSE_TYPE, GRADE_QUESTION_CODE));
  if (!grade) return rows;

  if (rootCauseRequired(choiceOn(grade))) return rows;

  return rows.filter(
    (row) => !isQuestion(row, ROOT_CAUSE_RESPONSE_TYPE, ROOT_CAUSE_QUESTION_CODE),
  );
}
