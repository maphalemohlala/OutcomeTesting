import { useEffect, useState } from 'react';
import { Al_sectionsService, Al_questionsService, Al_questionversionsService } from '../../generated';
import type { Al_sections } from '../../generated/models/Al_sectionsModel';
import { Al_sectionsal_ownerrole } from '../../generated/models/Al_sectionsModel';
import type { Al_questions } from '../../generated/models/Al_questionsModel';
import type { Al_questionversions } from '../../generated/models/Al_questionversionsModel';
import { Al_questionversionsal_responsetype } from '../../generated/models/Al_questionversionsModel';
import { inForce, OWNER_ROLE_LABEL } from './libraryStatus';

/**
 * The al_section columns AD-123 added. The generated Al_sections model predates them, and
 * generated files are refreshed by regenerating from Dataverse rather than edited by hand -
 * AD-032 records the same lag on al_auditevent. Declared here until the next
 * `pa app add data-source` run picks them up.
 */
type SectionSchedule = {
  al_effectivefrom?: string | null;
  al_effectiveto?: string | null;
  al_isoptional?: boolean | null;
};

export interface LibraryQuestion {
  id: string;
  code: string;
  order: number;
  wording: string;
  responseType: string;
  responseTypeValue: number;
  mandatory: boolean;
  versionNumber: number;
  /** Its current version is dated out, so it is no longer asked (AD-122). */
  retired: boolean;
}

/** Response-type options (value + label) for the question editor, from the generated choice map. */
export const RESPONSE_TYPE_OPTIONS: { value: number; label: string }[] = Object.entries(
  Al_questionversionsal_responsetype,
)
  .map(([value, label]) => ({ value: Number(value), label: String(label) }))
  .filter((option) => Number.isFinite(option.value))
  .sort((a, b) => a.value - b.value);

export interface LibrarySection {
  id: string;
  code: string;
  name: string;
  helpText: string;
  /** 'Tax', 'AQS' or 'Both' (AD-123). */
  ownerRole: string;
  ownerRoleValue: number;
  conditional: boolean;
  /** Its questions are not owed at submit (AD-123). */
  isOptional: boolean;
  /** Dated out, so it is no longer part of the checklist (AD-123). */
  retired: boolean;
  order: number;
  questions: LibraryQuestion[];
}

export type QuestionLibraryState =
  | { status: 'unavailable'; reason: string }
  | { status: 'loading' }
  | { status: 'ready'; sections: LibrarySection[] };

/**
 * The owning team. OWNER_ROLE_LABEL is consulted before the generated choice map because the
 * generated one predates Both (120910105) and would render it 'Unassigned'.
 */
function sectionOwner(record: Al_sections): string {
  return (
    OWNER_ROLE_LABEL[record.al_ownerrole] ??
    record.al_ownerrolename ??
    Al_sectionsal_ownerrole[record.al_ownerrole] ??
    'Unassigned'
  );
}

function responseType(record: Al_questionversions): string {
  return (
    record.al_responsetypename ??
    Al_questionversionsal_responsetype[record.al_responsetype] ??
    'Unspecified'
  );
}

/** The version a reviewer would answer today: the highest version number per question. */
function currentVersionByQuestion(
  versions: Al_questionversions[],
): Map<string, Al_questionversions> {
  const current = new Map<string, Al_questionversions>();
  for (const version of versions) {
    const questionId = version._al_questionid_value;
    if (!questionId) continue;
    const held = current.get(questionId);
    if (!held || version.al_versionnumber > held.al_versionnumber) {
      current.set(questionId, version);
    }
  }
  return current;
}

function build(
  sections: Al_sections[],
  questions: Al_questions[],
  versions: Al_questionversions[],
): LibrarySection[] {
  const currentVersion = currentVersionByQuestion(versions);
  const asOf = new Date();

  const questionsBySection = new Map<string, LibraryQuestion[]>();
  for (const question of questions) {
    const sectionId = question._al_sectionid_value;
    const version = currentVersion.get(question.al_questionid);
    if (!sectionId || !version) continue;

    const row: LibraryQuestion = {
      id: question.al_questionid,
      code: question.al_questioncode,
      order: version.al_displayorder ?? question.al_displayorder ?? 0,
      wording: version.al_questiontext?.trim() || question.al_name,
      responseType: responseType(version),
      responseTypeValue: version.al_responsetype,
      mandatory: version.al_ismandatory,
      versionNumber: version.al_versionnumber,
      // The current version is still the current one whether or not it is in force; a
      // retired question is shown as retired rather than hidden, because the library is
      // where an administrator sees what was taken out.
      retired: !inForce(version.al_effectivefrom, version.al_effectiveto, asOf),
    };
    const list = questionsBySection.get(sectionId) ?? [];
    list.push(row);
    questionsBySection.set(sectionId, list);
  }

  return sections
    .map((section) => {
      const schedule = section as Al_sections & SectionSchedule;
      return {
        id: section.al_sectionid,
        code: section.al_sectioncode,
        name: section.al_name,
        helpText: section.al_helptext ?? '',
        ownerRole: sectionOwner(section),
        ownerRoleValue: section.al_ownerrole,
        conditional: section.al_isconditional,
        // Absent reads as required: al_isoptional is null on every section that predates
        // AD-123, because Dataverse applies a boolean default to new rows only.
        isOptional: schedule.al_isoptional === true,
        retired: !inForce(schedule.al_effectivefrom, schedule.al_effectiveto, asOf),
        order: section.al_displayorder ?? 0,
        questions: (questionsBySection.get(section.al_sectionid) ?? []).sort(
          (a, b) => a.order - b.order,
        ),
      };
    })
    .sort((a, b) => a.order - b.order);
}

/**
 * Reads the published checklist structure (Section -> Question -> current Question Version)
 * so administrators can see the live library. Editing is a controlled retire-and-succeed
 * write path (FR-030/FR-031); `reloadKey` refreshes the view after a successful edit.
 */
export function useQuestionLibrary(reloadKey = 0): QuestionLibraryState {
  const [state, setState] = useState<QuestionLibraryState>({ status: 'loading' });

  useEffect(() => {
    let cancelled = false;

    Promise.all([
      Al_sectionsService.getAll({ top: 500 }),
      Al_questionsService.getAll({ top: 500 }),
      Al_questionversionsService.getAll({ top: 500 }),
    ])
      .then(([sections, questions, versions]) => {
        if (cancelled) return;
        if (!sections.success || !questions.success || !versions.success) {
          setState({
            status: 'unavailable',
            reason: 'The question library could not be loaded from Dataverse.',
          });
          return;
        }
        setState({
          status: 'ready',
          sections: build(sections.data, questions.data, versions.data),
        });
      })
      .catch(() => {
        if (cancelled) return;
        setState({
          status: 'unavailable',
          reason: 'The question library could not be loaded from Dataverse.',
        });
      });

    return () => {
      cancelled = true;
    };
  }, [reloadKey]);

  return state;
}
