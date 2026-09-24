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
import { ADVICE_DATE_LABEL } from '../cases/caseHeaderDates';
import { MIGRATED_LISTS, lookupLabelOn } from '../admin/listOptions';

/** MIGRATED_LISTS is the single place that says which lists have a lookup on the case. */
function managedList(key: string) {
  const found = MIGRATED_LISTS.find((list) => list.key === key);
  if (!found) throw new Error(`No migrated list '${key}'`);
  return found;
}
import { choiceLabel } from '../../lib/choiceLabel';
import { date, text } from '../../lib/format';
import { withoutUnaskedRootCause } from './gradingRules';
import { checkerLabel } from '../cases/checkerNames';
import { lookupLabel } from '../cases/lookupLabel';
import { REVIEW_ROUTES, type ReviewRoute } from '../../types/domain';
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
const ROOT_CAUSES: ChoiceOption[] = [
  { value: 120910320, label: 'FactFind quality' },
  { value: 120910321, label: 'Risk / capacity mismatch' },
  { value: 120910322, label: 'Research / rationale' },
  { value: 120910323, label: 'Charges / value' },
  { value: 120910324, label: 'Client communication' },
  { value: 120910325, label: 'Process / documentation' },
  { value: 120910326, label: 'AML / CRA' },
  { value: 120910327, label: 'Retirement Proposition' },
  { value: 120910328, label: 'Adviser judgement' },
];

/** The suitability scale, and the same scale with N/A (2026-09-24: CRP and Q-E4-03). */
export const PASS_FAIL_INSUFFICIENT = 120910006;
export const PASS_FAIL_INSUFFICIENT_NA = 120910012;

export const SCALE_OPTIONS: Record<number, ChoiceOption[]> = {
  120910005: [PASS, FAIL],
  120910006: [PASS, FAIL, INSUFFICIENT],
  120910007: [YES, NO],
  120910008: [YES, NO, NA],
  120910009: [YES, NO, INSUFFICIENT],
  120910010: [PASS, PASS_WITH_ISSUES, INSUFFICIENT, POTENTIAL_HARM],
  120910012: [PASS, FAIL, INSUFFICIENT, NA],
  120910003: ROOT_CAUSES,
  // Several root causes at once from 2026-09-24: Q-GR-02's successor version.
  120910013: ROOT_CAUSES,
  120910004: [
    { value: 120910340, label: 'LSA/LSDBA/TTFAC' },
    { value: 120910341, label: 'Trust' },
    { value: 120910342, label: 'IHT' },
    { value: 120910343, label: 'Tax calculation' },
    { value: 120910344, label: 'Other' },
  ],
};

/** The three-column scales that the document lays out as a tick grid. */
const GRID_SCALES = new Set([120910005, 120910006, 120910007, 120910008, 120910009, 120910012]);

/**
 * Whether a row on `rowScale` draws in the tick columns of a grid headed by `gridScale`.
 *
 * The same scale, or a plain suitability row on the N/A grid: the N/A scale is the
 * suitability scale with a fourth column, so the two share one table and a row that does not
 * offer N/A leaves that cell empty (2026-09-24). The portal and the emailed PDF read it alike.
 */
export function onGridScale(rowScale: number | null, gridScale: number | null): boolean {
  if (rowScale == null || gridScale == null) return false;
  return (
    rowScale === gridScale ||
    (gridScale === PASS_FAIL_INSUFFICIENT_NA && rowScale === PASS_FAIL_INSUFFICIENT)
  );
}

/** Whether the row's own scale offers this option, so its grid cell carries a box. */
export function offers(rowScale: number | null, option: ChoiceOption): boolean {
  return optionsFor(rowScale).some((own) => own.value === option.value);
}

export function optionsFor(
  responseTypeValue: number | null,
  isTaxReview = false,
): ChoiceOption[] {
  const options = responseTypeValue == null ? [] : (SCALE_OPTIONS[responseTypeValue] ?? []);
  if (!isTaxReview || responseTypeValue == null) return options;

  // A grid on a Tax review reads 120910302 as the Tax check does (AD-055 amended). Same
  // value, same scale, same saved answer - only the wording, and only on this discipline,
  // because 120910006 is shared with the suitability grid.
  //
  // Q-TAX-02 is drawn inline and relabelled by INLINE_LABELS, so this reaches only a section
  // added through checklist administration (AD-123) whose questions all share this scale.
  // Keyed on the review rather than the section's owner role because "for tax checks" is what
  // was reworded: a Both-owned section answered on a Tax review is a tax check, and a
  // Tax-owned section never appears on an AQS review. The portal makes the same reading.
  const overrides = INLINE_LABELS[responseTypeValue];
  if (!overrides) return options;
  return options.map((option) =>
    overrides[option.value] ? { ...option, label: overrides[option.value] } : option,
  );
}

/**
 * The outcome scales the document writes in upper case wherever it draws them inline:
 * "PASS / PASS WITH ISSUES / FAIL" on the tax check outcome, "PASS / FAIL" on the file
 * quality outcome, "YES / NO" on remedial action required, and the four-value grade. The
 * same values are headed in title case where a tick grid heads a column with them (Pass,
 * Fail, Insufficient evidence), so the case belongs to the inline rendering rather than to
 * the option, and only this path applies it. The root cause list is title case in the
 * document and is deliberately absent.
 */
const UPPER_CASE_INLINE = new Set([120910005, 120910006, 120910007, 120910010, 120910012]);

/**
 * The tax check outcome as the document orders it: PASS, PASS WITH ISSUES, FAIL. The
 * suitability grid heads the same scale Pass, Fail, Insufficient evidence, so the order is
 * the inline rendering's too. Only S-TAX answers this scale off a grid.
 */
const INLINE_ORDER: Record<number, number[]> = {
  120910006: [120910300, 120910302, 120910301],
};

/**
 * The labels the inline rendering gives a value in place of the scale's own, keyed by
 * response type and then by value.
 *
 * The Tax check reads 120910302 as "Pass with issues" where every other user of the same
 * scale still reads it as "Insufficient evidence" (AD-055 amended). This is wording, not a
 * value: what the checker ticks, what is saved, and what sends the case to remediation are
 * all exactly what they were.
 *
 * It belongs here rather than to the option because 120910006 is shared with the
 * suitability grid (S-E1 to S-E5, S-CRP), which heads its third column from SCALE_OPTIONS -
 * so renaming the option itself would have relabelled six AQS sections that were never
 * asked to change. ResponseRules.PermittedChoices is still the authority on which values
 * may be saved, and it is untouched.
 *
 * Applied only on a Tax review. This map was once described as the inline path's alone, on
 * the reading that only Q-TAX-02 was ever drawn inline at this scale; `inlineOptionsFor`
 * says what went wrong with that. Both readers of this map now test the discipline.
 */
const INLINE_LABELS: Record<number, Record<number, string>> = {
  120910006: { 120910302: 'Pass with issues' },
  120910012: { 120910302: 'Pass with issues' },
};

/**
 * The options of a row as the document draws them inline - a row of tick boxes beside the
 * question rather than a column of a grid. Same options, same values; the document's own
 * casing and order.
 *
 * The reorder and the rename below are the TAX CHECK's, not the scale's, so they are keyed
 * on the review's discipline exactly as `optionsFor` and the grid path key theirs. This
 * used to apply them to every 120910006 question drawn inline, on the reading that the
 * inline path was Q-TAX-02's alone. AD-123 checklist administration made that false: a
 * question retyped to this scale breaks its section's uniform grid, the section falls to
 * this path, and an AQS question was drawn as a tax check - "PASS / PASS WITH ISSUES /
 * FAIL" over the suitability scale's own values. The value saved was always right and the
 * wording over it was wrong, which is the worse half to get wrong.
 */
export function inlineOptionsFor(
  responseTypeValue: number | null,
  isTaxReview = false,
): ChoiceOption[] {
  const options = optionsFor(responseTypeValue);
  if (responseTypeValue == null || options.length === 0) return options;

  const order = isTaxReview ? INLINE_ORDER[responseTypeValue] : undefined;
  const ordered = order
    ? order
        .map((value) => options.find((option) => option.value === value))
        .filter((option): option is ChoiceOption => option !== undefined)
    : options;

  // Rename before upper-casing, so an override is cased by the same rule as the label it
  // replaces rather than having to be written in the document's case itself.
  const overrides = isTaxReview ? INLINE_LABELS[responseTypeValue] : undefined;
  const relabelled = overrides
    ? ordered.map((option) =>
        overrides[option.value] ? { ...option, label: overrides[option.value] } : option,
      )
    : ordered;

  return UPPER_CASE_INLINE.has(responseTypeValue)
    ? relabelled.map((option) => ({ ...option, label: option.label.toUpperCase() }))
    : relabelled;
}

/**
 * The number of columns the document lays an inline option list out in, or null for a
 * single row. Primary root cause is the one list it grids: nine causes in three rows of
 * three, read left to right.
 */
export function optionGridColumns(responseTypeValue: number | null): number | null {
  return responseTypeValue === 120910003 || responseTypeValue === 120910013 ? 3 : null;
}

/**
 * How a section lays out on the form. A section whose rows all share one tick scale is a
 * grid - one column per option, as E1 to E5, AML and CRA, CRP and Consumer Duty are on
 * the document. Anything else (Tax check, File Quality outcome, grading) is a row per
 * question with its ticks or value inline.
 */
export function sectionLayout(
  section: ReviewSection,
  isTaxReview = false,
): { kind: 'grid'; options: ChoiceOption[] } | { kind: 'inline' } {
  const first = section.rows[0]?.responseTypeValue;
  if (
    first != null &&
    GRID_SCALES.has(first) &&
    section.rows.every((row) => row.responseTypeValue === first)
  ) {
    return { kind: 'grid', options: optionsFor(first, isTaxReview) };
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

/** Whether the section's help text is the document's "Outcome lens" footer line (E1 to E5). */
export function isOutcomeLens(section: ReviewSection): boolean {
  return /^S-E\d/.test(section.code ?? '');
}

/**
 * Whether the row is a section's outcome-lens tick rather than a test point of its own.
 *
 * The document draws a single tick box on E2's outcome lens row and on no other lens row.
 * It is seeded as a question (Q-E2-LENS) so the tick is a recorded answer like any other,
 * and recognised by the -LENS code suffix rather than by the section, so a second one could
 * be added to the seed without touching either page.
 */
export function isLensTick(row: FormRow<SectionedAnswer>): boolean {
  return /-LENS$/.test(row.code ?? '');
}

// ---------------------------------------------------------------------------------------
// Case header block
// ---------------------------------------------------------------------------------------

export interface HeaderField {
  label: string;
  value: string | null;
}

/**
 * The case's review route, where it carries a recognised one.
 *
 * Read from the lookup's name rather than its id, which is what the record already holds.
 * An unrecognised or absent route reads as null, and `checkerNames` treats that as requiring
 * both checks - so an unallocated one reads as work outstanding rather than work nobody owes.
 */
function headerRoute(record: Al_outcomecases): ReviewRoute | null {
  const name = lookupLabel(record, 'al_reviewrouteid', record.al_reviewrouteidname);
  return REVIEW_ROUTES.find((route) => route === name) ?? null;
}

/**
 * The case header block, field for field in the order the document lays them out. These are
 * Outcome Case columns captured at intake, not checklist questions (checklist-v8.md).
 *
 * `products` is the one field the record cannot answer on its own: the products a case
 * covers are a many-to-many, so their names are resolved by the caller (heldOptionLabels)
 * and handed in. Absent, the field falls back to the free-text column cases imported before
 * the list carry - which is what every case showed until a product was ticked.
 */
export function caseHeaderFields(
  record: Al_outcomecases,
  products: readonly string[] = [],
): HeaderField[] {
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
    { label: 'Paraplanner', value: text(record.al_paraplanner) },
    {
      label: 'Product(s)',
      // The ticked products first, then the free text. Same precedence as the four lookups
      // below, for the same reason: reading only the new field would blank the header on
      // every case imported before the list existed.
      value: products.length > 0 ? products.join('; ') : text(record.al_products),
    },
    {
      label: 'Case type',
      value:
        lookupLabelOn(record as unknown as Record<string, unknown>, managedList('case-type')) ??
        choiceLabel(Al_outcomecasesal_casetype, record.al_casetype, record.al_casetypename),
    },
    { label: ADVICE_DATE_LABEL, value: date(record.al_advicedate) },
    {
      label: 'Product / solution type',
      // The managed-list lookup first, then the choice column a case imported before the
      // lookup existed still carries (AD-187). Not a transitional nicety: until every case
      // is backfilled, reading only the lookup would blank the field on the older ones.
      value:
        lookupLabelOn(record as unknown as Record<string, unknown>, managedList('product-solution-type')) ??
        choiceLabel(
          Al_outcomecasesal_productsolutiontype,
          record.al_productsolutiontype,
          record.al_productsolutiontypename,
        ),
    },
    {
      label: 'Sample source',
      value:
        lookupLabelOn(record as unknown as Record<string, unknown>, managedList('sample-source')) ??
        choiceLabel(
          Al_outcomecasesal_samplesource,
          record.al_samplesource,
          record.al_samplesourcename,
        ),
    },
    /*
     * Two checkers, not one (item 2, 2026-09-19). A case taking both a Tax check and an AQS
     * check has two, and the single "Checker name" the document draws named whichever was
     * allocated second. Each reflects the checker assigned to that review instance and is
     * stamped server-side by the allocation; neither is typed.
     *
     * This is the fourth deliberate difference from the reference Checker Checklist, which
     * has one Checker name field. checklistDocument.test.ts carries it as one.
     */
    {
      label: 'Tax Checker',
      value: checkerLabel(record.al_taxcheckername, headerRoute(record), 'Tax'),
    },
    {
      label: 'AQS Checker',
      value: checkerLabel(record.al_aqscheckername, headerRoute(record), 'AQS'),
    },
    { label: 'Check date', value: date(record.al_checkdate) },
    { label: 'Client name / initials', value: text(record.al_clientname) },
    // ClientRef, not the TaskID. al_casereference is the TaskID and is the page heading;
    // binding it here showed a checker the wrong identifier on every case (F6, 2026-09-20).
    { label: 'IO reference', value: text(record.al_ioreference) },
    {
      label: 'Pre or post check',
      value:
        lookupLabelOn(record as unknown as Record<string, unknown>, managedList('pre-or-post-check')) ??
        choiceLabel(
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
  /**
   * The document's row text, verbatim, straight off al_FailReason.al_Name - which holds the
   * whole row ("AML - ID verification issue"), category prefix included. The prefix is not
   * built here from al_Category, because the document does not punctuate the twenty rows
   * consistently: nineteen take a hyphen and the last an en-dash, and the two Tax check rows
   * run on in lower case. Reproducing that from a category label plus a separator would need
   * a per-reason separator column; storing the row as written needs none. al_Category stays
   * as the grouping it is, for MI and ordering.
   */
  label: string;
  ticked: boolean;
}

/**
 * The standalone File Quality fail points block: every reason on the list, ticked where it
 * is recorded against any answer on the review.
 *
 * Both teams get the whole list. The document draws one undivided twenty-row table, and the
 * category is a grouping of the reasons, not a split of who may pick them (project owner,
 * 2026-09-09). Filtering by category - Tax check to the Tax team, the rest to AQS, which is
 * what this did until now - was an inference from the prefixes, and it left a Tax reviewer
 * who found a record-keeping failure with nowhere to record it. Each team still records its
 * own ticks: they hang off that team's File quality outcome answer (Q-FQ-01 for AQS,
 * Q-FQTAX-01 for Tax) through the response-keyed intersect, so the two sets never collide.
 */
export function failPoints(reasons: FailReasonRef[], ticked: ReadonlySet<string>): FailPoint[] {
  return reasons
    .slice()
    .sort((a, b) => a.order - b.order || a.name.localeCompare(b.name))
    .map((reason) => ({
      id: reason.id,
      label: reason.name,
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
  /**
   * Prints no intro line even where the section carries help text. Consumer Duty's
   * "Short yes/no judgements only. Record any detail once in section H." went on
   * 2026-09-24 (project owner); the section H it named never existed in V8.
   */
  noIntro?: boolean;
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
    noIntro: true,
  },
  'S-GRADE': { id: 'grade', title: 'Checker judgement and grading', layout: 'inline' },
};

export interface FormGroup<T extends SectionedAnswer = SectionedAnswer> {
  id: string;
  /**
   * The subsection heading - the section's name alone, e.g. "Client Objectives & Information
   * (COBS 9.2)" (2026-09-24; it was "E1. Client ..."); null when the block has none.
   */
  heading: string | null;
  rows: FormRow<T>[];
  /** The document's "Outcome lens" line under the subsection; null when it has none. */
  lens: string | null;
  /**
   * The tick box the document draws on the lens row itself, which E2 has and no other
   * section does. Held apart from `rows` so it renders in the lens row's last cell rather
   * than taking a row of its own; null everywhere else.
   */
  lensTick: FormRow<T> | null;
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
      /**
       * The review's discipline, carried on the block so the inline path can read it off
       * what it is already given. `options` above is already discipline-aware; the rows an
       * inline block draws are not built here, so without this the renderer would have to
       * thread the flag down four component layers to reach `inlineOptionsFor`.
       */
      isTaxReview: boolean;
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
  isTaxReview = false,
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

    const lensTick = section.rows.find((row) => isLensTick(row)) ?? null;
    /*
     * The primary root cause is drawn only when the grade says the file did not pass
     * (item 3, 2026-09-19). Applied to the section's own rows because the grade and the
     * cause sit together in Judgement and Grading; every other section is handed straight
     * back. GradingRules enforces the same rule server-side, where it decides whether the
     * question is owed at submission and clears a cause recorded before the grade changed -
     * this only keeps the document from drawing a row the review does not owe.
     */
    const visible = withoutUnaskedRootCause(section.rows);
    const group: FormGroup<T> = {
      id: section.id,
      heading: spec?.subsections ? section.name : null,
      rows: lensTick ? visible.filter((row) => row !== lensTick) : visible,
      lens: spec?.subsections ? section.helpText : null,
      lensTick,
    };

    /**
     * Whether the section still answers on the scale its block declares.
     *
     * A seeded block names its own scale and tick columns above rather than deriving them,
     * so that the reference document pins them (AD-098, AD-104). That holds only while the
     * questions still answer on it, and AD-123 checklist administration can retype them:
     * S-AMLCRA had three of its five retyped to 120910006 and went on heading itself
     * Yes / No / N/A, describing the two it had left. The declared scale is a default, not
     * a promise about questions an administrator may since have changed.
     *
     * Read over the rows that answer on a tick scale, and those alone. A section may also
     * carry a written, dated or formatted question, and such a row has never had a cell
     * under these columns anyway - it is drawn inline by `onScale` either way. Counting it
     * here would take a block off its grid on the strength of a row that was never in it.
     * The portal reads the same rows.
     */
    const declared = spec?.layout === 'grid' ? (spec.scale ?? null) : null;
    const scaled = group.rows.filter(
      (row) => row.responseTypeValue != null && GRID_SCALES.has(row.responseTypeValue),
    );
    // The N/A scale widens the suitability scale rather than leaving it: a block declared on
    // one takes the other's rows, and a row on the N/A scale moves the block onto it.
    const widened =
      declared === PASS_FAIL_INSUFFICIENT &&
      scaled.some((row) => row.responseTypeValue === PASS_FAIL_INSUFFICIENT_NA);
    const fitsDeclared =
      declared == null ||
      scaled.length === 0 ||
      scaled.every(
        (row) =>
          row.responseTypeValue === declared ||
          (declared === PASS_FAIL_INSUFFICIENT &&
            row.responseTypeValue === PASS_FAIL_INSUFFICIENT_NA),
      );

    const last = blocks[blocks.length - 1];
    if (spec && last && last.kind === 'section' && last.id === spec.id) {
      last.groups.push(group);
      // One block draws one set of tick columns, so it cannot head half of itself: a later
      // subsection that has left the scale takes the whole block off it. Suitability core
      // checks is the only block built from more than one section.
      if (!fitsDeclared && last.layout === 'grid') {
        last.layout = 'inline';
        last.options = [];
      } else if (widened && last.layout === 'grid') {
        last.options = optionsFor(PASS_FAIL_INSUFFICIENT_NA, isTaxReview);
      }
      continue;
    }

    if (spec) {
      // Off its declared scale, the block heads itself from its own questions on exactly
      // the terms an added section is read on: one shared scale is a grid of that scale,
      // and a section mixing two is a meta table, where each row draws its own options and
      // no column header can misdescribe it.
      const derived = fitsDeclared ? null : sectionLayout(section, isTaxReview);
      blocks.push({
        kind: 'section',
        id: spec.id,
        title: spec.title,
        intro: spec.noIntro ? null : (spec.intro ?? (spec.subsections ? null : section.helpText)),
        layout: derived ? derived.kind : spec.layout,
        columnHeading: spec.columnHeading ?? 'Check',
        options: derived
          ? derived.kind === 'grid'
            ? derived.options
            : []
          : spec.scale == null
            ? []
            : optionsFor(widened ? PASS_FAIL_INSUFFICIENT_NA : spec.scale, isTaxReview),
        groups: [group],
        isTaxReview,
      });
      continue;
    }

    const layout = sectionLayout(section, isTaxReview);
    blocks.push({
      kind: 'section',
      id: section.id,
      title: section.name,
      intro: section.helpText,
      layout: layout.kind,
      columnHeading: 'Check',
      options: layout.kind === 'grid' ? layout.options : [],
      groups: [group],
      isTaxReview,
    });
  }

  if (!placed) blocks.push(failPoints);
  return blocks;
}


/**
 * What the paraplanner ticked in the Intelligent Office task, and who stamped it.
 *
 * `al_checklistitems` is written by the import as the selected item names, one per line
 * (`ImportRules.ReadChecklist`), and is the only input to the BR-004 route. It is the
 * answer to "why is this case being checked at all", which nothing displayed until now.
 *
 * Only the ticked items are stored, so only the ticked items can be shown: the seven-item
 * vocabulary lives in the plug-in assembly and the case does not record which of them were
 * offered and declined.
 *
 * Blank lines are dropped rather than rendered as empty bullets - a trailing newline is
 * ordinary in a memo column, and the import joins with "\n" without trimming the result.
 */
export interface CaseChecklist {
  /** The ticked items, in the order the import wrote them. */
  items: string[];
  completedBy: string | null;
  completedOn: string | null;
}

export function caseChecklist(record: Al_outcomecases): CaseChecklist {
  const raw = record.al_checklistitems;
  const items =
    typeof raw === 'string'
      ? raw
          .split('\n')
          .map((line) => line.trim())
          .filter((line) => line !== '')
      : [];

  return {
    items,
    completedBy: record.al_checklistcompletedby?.trim() || null,
    completedOn: record.al_checklistcompleteddate ?? null,
  };
}
