/**
 * Builds the checklist form for one review: every section the reviewing team owns, every
 * question in force in that section, and the answer recorded against each - or none.
 *
 * The form shows every row whether or not it has been answered, as the Checker Checklist
 * document does; an unanswered row is an empty tick box, not a missing line. Question
 * display order is numbered within each section - Q-TAX-01 and Q-E1-01 are both order 1 -
 * so the section carries the cross-section order and the question order only means
 * anything inside its section.
 */

export interface SectionRef {
  id: string;
  /** The seed code (S-TAX, S-FQOUT ...), which is how the page finds the File Quality outcome. */
  code: string | null;
  name: string;
  order: number;
  /** The section's guidance line: the document's "Outcome lens" for E1 to E5. */
  helpText: string | null;
}

export interface QuestionRef {
  id: string;
  sectionId: string | null;
  order: number;
  /** The seed code (Q-E1-01, Q-E2-LENS ...); how the page recognises the outcome-lens tick. */
  code: string | null;
}

/** A question version in force on the review's reference day. */
export interface VersionRef {
  id: string;
  questionId: string | null;
  order: number;
  text: string;
  responseTypeValue: number | null;
  responseType: string;
  mandatory: boolean;
}

/** The subset of a recorded answer the builder needs; the page's full row type extends it. */
export interface SectionedAnswer {
  id: string;
  versionId: string | null;
  question: string;
  responseTypeValue: number | null;
  responseType: string;
}

export interface FormRow<T extends SectionedAnswer = SectionedAnswer> {
  key: string;
  question: string;
  responseTypeValue: number | null;
  responseType: string;
  mandatory: boolean;
  /** The question's seed code, where it could be read; null for an unplaced answer. */
  code: string | null;
  /** Null when nothing has been recorded against the question yet. */
  response: T | null;
}

export interface ReviewSection<T extends SectionedAnswer = SectionedAnswer> {
  id: string;
  code: string | null;
  name: string;
  helpText: string | null;
  rows: FormRow<T>[];
}

/** Where an answer lands when its version, question or section row could not be read. */
export const OTHER_SECTION_ID = 'other';

export function buildSections<T extends SectionedAnswer>(
  sections: SectionRef[],
  questions: QuestionRef[],
  versions: VersionRef[],
  responses: T[],
): ReviewSection<T>[] {
  const questionsBySection = new Map<string, QuestionRef[]>();
  for (const question of questions) {
    if (!question.sectionId) continue;
    const list = questionsBySection.get(question.sectionId) ?? [];
    list.push(question);
    questionsBySection.set(question.sectionId, list);
  }

  const versionsByQuestion = new Map<string, VersionRef[]>();
  for (const version of versions) {
    if (!version.questionId) continue;
    const list = versionsByQuestion.get(version.questionId) ?? [];
    list.push(version);
    versionsByQuestion.set(version.questionId, list);
  }

  const responseByVersion = new Map<string, T>();
  const unplaced = new Map<string, T>();
  for (const response of responses) {
    if (response.versionId) responseByVersion.set(response.versionId, response);
    unplaced.set(response.id, response);
  }

  const ordered = [...sections]
    .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name))
    .map<ReviewSection<T>>((section) => {
      const rows: { order: number; row: FormRow<T> }[] = [];
      for (const question of questionsBySection.get(section.id) ?? []) {
        for (const version of versionsByQuestion.get(question.id) ?? []) {
          const response = responseByVersion.get(version.id) ?? null;
          if (response) unplaced.delete(response.id);
          rows.push({
            order: version.order || question.order,
            row: {
              key: version.id,
              question: version.text,
              responseTypeValue: version.responseTypeValue,
              responseType: version.responseType,
              mandatory: version.mandatory,
              code: question.code,
              response,
            },
          });
        }
      }
      return {
        id: section.id,
        code: section.code,
        name: section.name,
        helpText: section.helpText,
        rows: rows.sort((a, b) => a.order - b.order).map((entry) => entry.row),
      };
    })
    .filter((section) => section.rows.length > 0);

  // An answer whose question is not on this team's form - its section belongs to the other
  // team, or reference data failed to load - is still shown: a recorded answer must never
  // disappear because the structure around it could not be read.
  if (unplaced.size > 0) {
    ordered.push({
      id: OTHER_SECTION_ID,
      code: null,
      name: 'Other answers',
      helpText: null,
      rows: [...unplaced.values()].map((response) => ({
        key: response.id,
        question: response.question,
        responseTypeValue: response.responseTypeValue,
        responseType: response.responseType,
        mandatory: false,
        code: null,
        response,
      })),
    });
  }

  return ordered;
}
