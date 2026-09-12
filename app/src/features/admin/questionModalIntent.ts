/**
 * Which command a question edit turns into (AD-122). The editor offers five fields, and they
 * do not all mean the same thing:
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
  responseType: number;
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
  // change here would let a blank textarea look like something worth a new version.
  const wording = draft.wording.trim();
  const changed =
    (wording.length > 0 && wording !== original.wording.trim()) ||
    draft.responseType !== original.responseType ||
    draft.mandatory !== original.mandatory ||
    draft.displayOrder !== original.displayOrder;

  return changed ? 'version' : 'none';
}
