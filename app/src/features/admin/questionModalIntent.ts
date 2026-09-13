/**
 * Which command a question edit turns into (AD-122), and what stops it being sent. The
 * editor offers five fields, and they do not all mean the same thing:
 *
 * - wording, response type, mandatory and display order are **version**-scoped, so changing
 *   any of them is a new version through `al_RetireAndSucceedQuestion`;
 * - section is **question**-scoped and not versioned, so changing it is a move — retire here,
 *   create there — through `al_MoveQuestion`, because an in-place lookup change would re-file
 *   every answer ever recorded under a section it was never answered in.
 *
 * Held apart from the modal so the decision can be tested; the form itself is markup.
 */

export interface QuestionDraft {
  wording: string;
  sectionId: string;
  /**
   * Null until the administrator picks one. There is no default: the response type decides
   * how the question can be answered and what the grading logic can read off it, and a
   * silent default makes that choice on their behalf — `al_AddQuestion` accepts any integer
   * it parses, so nothing downstream would catch it either.
   */
  responseType: number | null;
  mandatory: boolean;
  displayOrder: number;
}

export type QuestionEditIntent = 'none' | 'version' | 'move';

export function intentFor(draft: QuestionDraft, original: QuestionDraft): QuestionEditIntent {
  // A move carries the wording, response type and mandatory flag forward from the version it
  // retires, so it wins outright: the other edits in the draft are not applied, and the modal
  // says so rather than letting them look saved.
  if (draft.sectionId !== original.sectionId) {
    return 'move';
  }

  // An emptied wording is not an edit. The modal refuses to save it, and reporting it as a
  // change here would let a blank textarea look like something worth a new version. An
  // unpicked response type is read the same way, for the same reason.
  const wording = draft.wording.trim();
  const changed =
    (wording.length > 0 && wording !== original.wording.trim()) ||
    (draft.responseType !== null && draft.responseType !== original.responseType) ||
    draft.mandatory !== original.mandatory ||
    draft.displayOrder !== original.displayOrder;

  return changed ? 'version' : 'none';
}

export interface QuestionSubmission {
  mode: 'add' | 'edit';
  intent: QuestionEditIntent;
  draft: QuestionDraft;
  questionCode: string;
  reason: string;
}

/**
 * Why this draft cannot be sent, or null when it can. Checked here as well as server-side,
 * where the same rules are enforced properly: a half-filled form should cost a keystroke
 * rather than a round trip that comes back refused (AD-041).
 *
 * The order follows the fields down the form, so the message names the first thing the
 * administrator would see if they looked.
 */
export function refusalFor(submission: QuestionSubmission): string | null {
  const { mode, intent, draft, questionCode, reason } = submission;
  const isMove = intent === 'move';

  if (!draft.wording.trim()) {
    return 'Enter the question wording.';
  }

  // Not asked for on a move: that carries the response type forward from the version it
  // retires, so the control is disabled and there is nothing for the caller to have picked.
  if (!isMove && draft.responseType === null) {
    return 'Choose how the question is answered. The response type decides what a reviewer can record against it, so there is no default.';
  }

  if (mode === 'add' && !questionCode.trim()) {
    return 'Enter a question code. Codes are unique and are never reused.';
  }

  if (isMove && !questionCode.trim()) {
    return 'Enter a new code for the question in its new section. Codes are never reused.';
  }

  if (isMove && !reason.trim()) {
    return 'Say why the question is moving. The reason is recorded on the audit trail.';
  }

  return null;
}
