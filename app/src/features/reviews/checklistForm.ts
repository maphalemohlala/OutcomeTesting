import {
  Al_outcomecasesal_adviserstatus,
  Al_outcomecasesal_casetype,
  Al_outcomecasesal_preorpostcheck,
  Al_outcomecasesal_productsolutiontype,
  Al_outcomecasesal_samplesource,
  Al_outcomecasesal_taxcheckrequired,
  Al_outcomecasesal_taxteamdisposition,
  Al_outcomecasesal_vulnerableclient,
  type Al_outcomecases,
} from '../../generated/models/Al_outcomecasesModel';
import { choiceLabel } from '../../lib/choiceLabel';
import { date, text } from './reviewAnswer';
import type { FormRow, ReviewSection, SectionedAnswer } from './reviewSections';

/**
 * The Checker Checklist document as a form: the vocabulary each block draws on and the
 * pure mappings that fill it. Kept free of the generated services so it is unit testable.
 */

export interface ChoiceOption {
  value: number;
  label: string;
}

const PASS = { value: 120910300, label: 'Pass' };
const FAIL = { value: 120910301, label: 'Fail' };
const INSUFFICIENT = { value: 120910302, label: 'Insufficient evidence' };
const PASS_WITH_ISSUES = { value: 120910303, label: 'Pass with issues' };
const POTENTIAL_HARM = { value: 120910304, label: 'Potential harm' };
const YES = { value: 120910305, label: 'Yes' };
const NO = { value: 120910306, label: 'No' };
const NA = { value: 120910307, label: 'N/A' };

/**
 * The permitted options per response type - the AD-023 subset, mirrored from
 * ResponseRules.PermittedChoices, which is the authority. Single select is the Primary
 * root cause list (AD-055); multi select is the Tax check reason list.
 */
export const SCALE_OPTIONS: Record<number, ChoiceOption[]> = {
  120910005: [PASS, FAIL],
  120910006: [PASS, FAIL, INSUFFICIENT],
  120910007: [YES, NO],
  120910008: [YES, NO, NA],
  120910009: [YES, NO, INSUFFICIENT],
  120910010: [PASS, PASS_WITH_ISSUES, INSUFFICIENT, POTENTIAL_HARM],
  120910003: [
    { value: 120910320, label: 'FactFind quality' },
    { value: 120910321, label: 'Risk / capacity mismatch' },
    { value: 120910322, label: 'Research / rationale' },
    { value: 120910323, label: 'Charges / value' },
    { value: 120910324, label: 'Client communication' },
    { value: 120910325, label: 'Process / documentation' },
    { value: 120910326, label: 'AML / CRA' },
    { value: 120910327, label: 'Retirement Proposition' },
    { value: 120910328, label: 'Adviser judgement' },
  ],
  120910004: [
    { value: 120910340, label: 'LSA/LSDBA/TTFAC' },
    { value: 120910341, label: 'Trust' },
    { value: 120910342, label: 'IHT' },
    { value: 120910343, label: 'Tax calculation' },
    { value: 120910344, label: 'Other' },
  ],
};

/** The three-column scales that the document lays out as a tick grid. */
const GRID_SCALES = new Set([120910005, 120910006, 120910007, 120910008, 120910009]);

export function optionsFor(responseTypeValue: number | null): ChoiceOption[] {
  return responseTypeValue == null ? [] : (SCALE_OPTIONS[responseTypeValue] ?? []);
}

/**
 * How a section lays out on the form. A section whose rows all share one tick scale is a
 * grid - one column per option, as E1 to E5, AML and CRA, CRP and Consumer Duty are on
 * the document. Anything else (Tax check, File Quality outcome, grading) is a row per
 * question with its ticks or value inline.
 */
export function sectionLayout(
  section: ReviewSection,
): { kind: 'grid'; options: ChoiceOption[] } | { kind: 'inline' } {
  const first = section.rows[0]?.responseTypeValue;
  if (
    first != null &&
    GRID_SCALES.has(first) &&
    section.rows.every((row) => row.responseTypeValue === first)
  ) {
    return { kind: 'grid', options: optionsFor(first) };
  }
  return { kind: 'inline' };
}

export interface TickedAnswer extends SectionedAnswer {
  answerChoice: number | null;
  answerChoices: number[];
}

/** Whether the option is the one recorded (or one of those recorded) on the row. */
export function isTicked(row: FormRow<TickedAnswer>, option: ChoiceOption): boolean {
  const response = row.response;
  if (!response) return false;
  return response.answerChoice === option.value || response.answerChoices.includes(option.value);
}

/** The section codes of the File Quality outcome blocks, one per team (checklist-v8.md). */
export const FILE_QUALITY_OUTCOME_CODES = new Set(['S-FQOUT', 'S-FQTAX']);

/** Whether the section's help text is the document's "Outcome lens" footer line (E1 to E5). */
export function isOutcomeLens(section: ReviewSection): boolean {
  return /^S-E\d/.test(section.code ?? '');
}

// ---------------------------------------------------------------------------------------
// Case header block
// ---------------------------------------------------------------------------------------

export interface HeaderField {
  label: string;
  value: string | null;
}

/**
 * The case header block, field for field in the order the document lays them out. These are
 * Outcome Case columns captured at intake, not checklist questions (checklist-v8.md).
 */
export function caseHeaderFields(record: Al_outcomecases): HeaderField[] {
  return [
    { label: 'Adviser name', value: text(record.al_advisername) },
    {
      label: 'Adviser status',
      value: choiceLabel(
        Al_outcomecasesal_adviserstatus,
        record.al_adviserstatus,
        record.al_adviserstatusname,
      ),
    },
    { label: 'Adviser code', value: text(record.al_advisercode) },
    { label: 'Paraplanner', value: text(record.al_paraplanner) },
    { label: 'Paraplanner code', value: text(record.al_paraplannercode) },
    { label: 'Product(s)', value: text(record.al_products) },
    {
      label: 'Case type',
      value: choiceLabel(Al_outcomecasesal_casetype, record.al_casetype, record.al_casetypename),
    },
    { label: 'Advice date', value: date(record.al_advicedate) },
    {
      label: 'Product / solution type',
      value: choiceLabel(
        Al_outcomecasesal_productsolutiontype,
        record.al_productsolutiontype,
        record.al_productsolutiontypename,
      ),
    },
    {
      label: 'Sample source',
      value: choiceLabel(
        Al_outcomecasesal_samplesource,
        record.al_samplesource,
        record.al_samplesourcename,
      ),
    },
    { label: 'Checker name', value: text(record.al_checkername) },
    { label: 'Check date', value: date(record.al_checkdate) },
    { label: 'Client name / initials', value: text(record.al_clientname) },
    { label: 'IO reference', value: text(record.al_casereference) },
    {
      label: 'Pre or post check',
      value: choiceLabel(
        Al_outcomecasesal_preorpostcheck,
        record.al_preorpostcheck,
        record.al_preorpostcheckname,
      ),
    },
    {
      label: 'Vulnerable client?',
      value: choiceLabel(
        Al_outcomecasesal_vulnerableclient,
        record.al_vulnerableclient,
        record.al_vulnerableclientname,
      ),
    },
    {
      label: 'Tax check required',
      value: choiceLabel(
        Al_outcomecasesal_taxcheckrequired,
        record.al_taxcheckrequired,
        record.al_taxcheckrequiredname,
      ),
    },
    {
      label: 'For Tax team usage',
      value: choiceLabel(
        Al_outcomecasesal_taxteamdisposition,
        record.al_taxteamdisposition,
        record.al_taxteamdispositionname,
      ),
    },
  ];
}

// ---------------------------------------------------------------------------------------
// File Quality - Fail points
// ---------------------------------------------------------------------------------------

export interface FailReasonRef {
  id: string;
  name: string;
  category: string | null;
  categoryValue: number | null;
  order: number;
}

export interface FailPoint {
  id: string;
  /** "Category - reason", as the document lists it. */
  label: string;
  ticked: boolean;
}

/** The al_failreason category value that belongs to the Tax team (checklist-v8.md). */
const TAX_CHECK_CATEGORY = 120910403;

/**
 * The standalone File Quality fail points block: the team's own reasons, every one of them,
 * ticked where it is recorded against any answer on the review. The category is the team
 * split - Tax check is the Tax team's, everything else the AQS checker's, so a category
 * added later lands with AQS rather than vanishing from both.
 */
export function failPoints(
  reasons: FailReasonRef[],
  ticked: ReadonlySet<string>,
  reviewType: string,
): FailPoint[] {
  const isTax = reviewType === 'Tax';
  return reasons
    .filter((reason) =>
      isTax
        ? reason.categoryValue === TAX_CHECK_CATEGORY
        : reason.categoryValue !== TAX_CHECK_CATEGORY,
    )
    .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name))
    .map((reason) => ({
      id: reason.id,
      label: reason.category ? `${reason.category} - ${reason.name}` : reason.name,
      ticked: ticked.has(reason.id),
    }));
}

// ---------------------------------------------------------------------------------------
// Remediation and escalation
// ---------------------------------------------------------------------------------------

export interface RemediationLine {
  id: string;
  issue: string;
  remedialAction: string | null;
  owner: string | null;
  targetDate: string | null;
  signOff: string | null;
}

export interface RemediationSummary {
  lines: RemediationLine[];
  clientContactRequired: string | null;
  recheckRequired: string | null;
  changesAdvice: string | null;
  allApproved: string | null;
  regradedOutcome: string | null;
  supervisorSignOff: string | null;
  adviserSignOff: string | null;
}

interface ActionLike {
  id: string;
  reference: string;
  description: string;
  remedialAction: string | null;
  assignedTo: string | null;
  owner: string | null;
  dueOn: string | null;
  completedOn: string | null;
  clientContactRequired: string | null;
  recheckRequired: string | null;
  changesAdvice: string | null;
}

interface SignoffLike {
  decision: string;
  signedOffOn: string | null;
  remediationAction: string | null;
  signedOffBy: string | null;
}

interface OutcomeLike {
  finalOutcome: string | null;
  regradedOn: string | null;
}

/** The distinct recorded values, joined; null when nothing is recorded on any action. */
function distinct(values: (string | null)[]): string | null {
  const seen = [...new Set(values.filter((value): value is string => value !== null))];
  return seen.length > 0 ? seen.join(', ') : null;
}

/**
 * The remediation block as the document lays it out (AD-095): one numbered line per action,
 * then the case-level judgements read off the actions and the sign-offs. "All remedial
 * actions checked and approved" is derived from every action's latest sign-off so it cannot
 * disagree with them.
 */
export function remediationSummary(
  actions: ActionLike[],
  signoffs: SignoffLike[],
  outcomes: OutcomeLike[],
): RemediationSummary {
  const latestByAction = new Map<string, SignoffLike>();
  for (const signoff of signoffs) {
    if (signoff.remediationAction) latestByAction.set(signoff.remediationAction, signoff);
  }

  const lines = actions.map<RemediationLine>((action) => {
    const signoff = latestByAction.get(action.reference);
    return {
      id: action.id,
      issue: action.description,
      remedialAction: action.remedialAction,
      owner: action.assignedTo ?? action.owner,
      targetDate: action.dueOn,
      signOff: signoff
        ? [signoff.decision, signoff.signedOffOn].filter(Boolean).join(' ')
        : action.completedOn
          ? `Completed ${action.completedOn}`
          : null,
    };
  });

  const allApproved =
    actions.length === 0
      ? null
      : actions.every((action) => latestByAction.get(action.reference)?.decision === 'Approved')
        ? 'Yes'
        : 'No';

  const latestSignoff = signoffs.length > 0 ? signoffs[signoffs.length - 1] : null;
  const regraded = outcomes.find((outcome) => outcome.finalOutcome) ?? null;
  const completed = actions.filter((action) => action.completedOn);

  return {
    lines,
    clientContactRequired: distinct(actions.map((action) => action.clientContactRequired)),
    recheckRequired: distinct(actions.map((action) => action.recheckRequired)),
    changesAdvice: distinct(actions.map((action) => action.changesAdvice)),
    allApproved,
    regradedOutcome: regraded
      ? [regraded.finalOutcome, regraded.regradedOn].filter(Boolean).join(' - ')
      : null,
    supervisorSignOff: latestSignoff
      ? [latestSignoff.signedOffBy, latestSignoff.signedOffOn].filter(Boolean).join(', ') || null
      : null,
    adviserSignOff:
      completed.length > 0
        ? distinct(
            completed.map((action) =>
              [action.assignedTo, action.completedOn].filter(Boolean).join(', '),
            ),
          )
        : null,
  };
}

// ---------------------------------------------------------------------------------------
// The document's blocks
// ---------------------------------------------------------------------------------------

/**
 * The Checker Checklist lays its sections out under its own headings, which are not the
 * section names: E1 to E5 sit together under "Suitability core checks" as one table headed
 * "Suitability test point", the Tax check is "File Quality - Tax check section", and so on.
 * The checklist model holds Section then Question with no grouping level, so the grouping
 * is a reading of the section code here, in one place, and the portal's review page makes
 * the same reading (AD-098).
 */
interface BlockSpec {
  id: string;
  title: string;
  intro?: string;
  layout: 'grid' | 'inline';
  columnHeading?: string;
  scale?: number;
  /** Each section in the block is a subsection with its own heading row and outcome lens. */
  subsections?: boolean;
}

const SUITABILITY: BlockSpec = {
  id: 'suitability',
  title: 'Suitability core checks',
  intro: 'Suitability core checks are shown in a consistent Pass/Fail format against each test point.',
  layout: 'grid',
  columnHeading: 'Suitability test point',
  scale: 120910006,
  subsections: true,
};

const FILE_QUALITY_OUTCOME: BlockSpec = { id: 'fq', title: 'File Quality Outcome', layout: 'inline' };

const BLOCKS: Record<string, BlockSpec> = {
  'S-TAX': { id: 'tax', title: 'File Quality - Tax check section', layout: 'inline' },
  'S-AMLCRA': {
    id: 'amlcra',
    title: 'File Quality - AML and CRA checking points',
    layout: 'grid',
    columnHeading: 'Check',
    scale: 120910008,
  },
  'S-FQOUT': FILE_QUALITY_OUTCOME,
  'S-FQTAX': FILE_QUALITY_OUTCOME,
  'S-E1': SUITABILITY,
  'S-E2': SUITABILITY,
  'S-E3': SUITABILITY,
  'S-E4': SUITABILITY,
  'S-E5': SUITABILITY,
  'S-CRP': {
    id: 'crp',
    title: 'Centralised Retirement Proposition',
    intro: 'Complete this section where retirement income planning or decumulation advice is in scope.',
    layout: 'grid',
    columnHeading: 'Centralised Retirement Proposition test point',
    scale: 120910006,
  },
  'S-CD': {
    id: 'cd',
    title: 'Consumer Duty overlay',
    layout: 'grid',
    columnHeading: 'Outcome',
    scale: 120910009,
  },
  'S-GRADE': { id: 'grade', title: 'Checker judgement and grading', layout: 'inline' },
};

export interface FormGroup<T extends SectionedAnswer = SectionedAnswer> {
  id: string;
  /** The subsection heading, e.g. "E1. Client Objectives & Information (COBS 9.2)"; null when the block has none. */
  heading: string | null;
  rows: FormRow<T>[];
  /** The document's "Outcome lens" line under the subsection; null when it has none. */
  lens: string | null;
}

export type FormBlock<T extends SectionedAnswer = SectionedAnswer> =
  | {
      kind: 'section';
      id: string;
      title: string;
      intro: string | null;
      layout: 'grid' | 'inline';
      columnHeading: string;
      options: ChoiceOption[];
      groups: FormGroup<T>[];
    }
  | { kind: 'failpoints'; id: 'failpoints'; title: string; points: FailPoint[] };

/**
 * The form as the document lays it out: the sections folded into the document's blocks, in
 * section order, with the standalone fail points placed directly before File Quality
 * Outcome. A section whose code the document does not know keeps its own name and lays out
 * by its rows, so a section added later still renders.
 */
export function formBlocks<T extends SectionedAnswer>(
  sections: ReviewSection<T>[],
  points: FailPoint[],
): FormBlock<T>[] {
  const blocks: FormBlock<T>[] = [];
  const failPoints: FormBlock<T> = {
    kind: 'failpoints',
    id: 'failpoints',
    title: 'File Quality – Fail points',
    points,
  };
  let placed = false;

  for (const section of sections) {
    const spec = section.code ? BLOCKS[section.code] : undefined;

    if (spec === FILE_QUALITY_OUTCOME && !placed) {
      blocks.push(failPoints);
      placed = true;
    }

    const group: FormGroup<T> = {
      id: section.id,
      heading:
        spec?.subsections && section.code
          ? `${section.code.replace(/^S-/, '')}. ${section.name}`
          : null,
      rows: section.rows,
      lens: spec?.subsections ? section.helpText : null,
    };

    const last = blocks[blocks.length - 1];
    if (spec && last && last.kind === 'section' && last.id === spec.id) {
      last.groups.push(group);
      continue;
    }

    if (spec) {
      blocks.push({
        kind: 'section',
        id: spec.id,
        title: spec.title,
        intro: spec.intro ?? (spec.subsections ? null : section.helpText),
        layout: spec.layout,
        columnHeading: spec.columnHeading ?? 'Check',
        options: spec.scale == null ? [] : optionsFor(spec.scale),
        groups: [group],
      });
      continue;
    }

    const layout = sectionLayout(section);
    blocks.push({
      kind: 'section',
      id: section.id,
      title: section.name,
      intro: section.helpText,
      layout: layout.kind,
      columnHeading: 'Check',
      options: layout.kind === 'grid' ? layout.options : [],
      groups: [group],
    });
  }

  if (!placed) blocks.push(failPoints);
  return blocks;
}
