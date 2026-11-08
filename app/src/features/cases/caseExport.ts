import { ukDayOf } from './caseHeaderDates';
import type { CellValue } from '../../lib/tabular';
import { ADVICE_DATE_LABEL } from './caseHeaderDates';
import { UPLOADED_BY_LABEL } from '../../types/domain';
import type { CaseSummary } from './caseWorklistMapping';

/**
 * The case extract used by the worklist, the person view and the full data extract.
 * Business column names only — no Dataverse logical names or GUIDs, which the design
 * skill forbids on a non-administrative surface.
 */
export const CASE_EXPORT_HEADERS = [
  'Case reference',
  'Status',
  'Review route',
  'Priority',
  UPLOADED_BY_LABEL,
  'Client name',
  'Adviser name',
  'Adviser code',
  'Paraplanner name',
  'Paraplanner code',
  'Tax Checker',
  'AQS Checker',
  'Case type',
  'Product / solution type',
  'Products',
  ADVICE_DATE_LABEL,
  'Check date',
  'Pre or post check',
  'Imported on',
  'Due date',
  'Age (days)',
  'Initial outcome',
  'Final outcome',
  'Finalised on',
];

/** Dates are exported as the calendar day only; a time component is noise in a report. */
function day(iso: string | null): string {
  return iso ? iso.slice(0, 10) : '';
}

/** A timestamp's UK day, so an export agrees with the date filter that chose the row. */
function ukDate(iso: string | null): string {
  return iso ? (ukDayOf(iso) ?? '') : '';
}

export function caseExportRow(item: CaseSummary): CellValue[] {
  return [
    item.caseReference,
    item.status,
    item.route ?? '',
    item.priority ?? '',
    item.owner ?? '',
    item.client ?? '',
    item.adviser ?? '',
    item.adviserCode ?? '',
    item.paraplanner ?? '',
    item.paraplannerCode ?? '',
    item.taxChecker ?? '',
    item.aqsChecker ?? '',
    item.caseType ?? '',
    item.productSolutionType ?? '',
    item.products ?? '',
    day(item.adviceDate),
    day(item.checkDate),
    item.preOrPostCheck ?? '',
    ukDate(item.createdOn),
    day(item.dueDate),
    item.ageInDays,
    item.initialOutcome ?? '',
    item.finalOutcome ?? '',
    ukDate(item.finalisedOn),
  ];
}

/**
 * The completed-case report: graded cases with who checked them, what each check found and
 * how long each stage took (BR-010).
 */
export const COMPLETED_CASE_HEADERS = [
  'Case reference',
  'Client name',
  'Adviser name',
  'Adviser code',
  'Paraplanner name',
  'Paraplanner code',
  'Tax Checker',
  'AQS Checker',
  'Case type',
  'Product / solution type',
  'Products',
  'Received on',
  'Check date',
  'Pre or post check',
  'Tax outcome',
  'Initial outcome',
  'Final outcome',
  'Outcome',
  'Finalised on',
  'Days in Tax',
  'Days in AQS',
  'Overall age (days)',
  'Status',
];

/** Whole days between two timestamps, blank where either is missing. */
function daysBetween(from: string | null | undefined, to: string | null | undefined): number | '' {
  if (!from || !to) return '';
  return Math.max(0, Math.floor((new Date(to).getTime() - new Date(from).getTime()) / 86_400_000));
}

export function completedCaseRow(item: CaseSummary): CellValue[] {
  return [
    item.caseReference,
    item.client ?? '',
    item.adviser ?? '',
    item.adviserCode ?? '',
    item.paraplanner ?? '',
    item.paraplannerCode ?? '',
    item.taxChecker ?? '',
    item.aqsChecker ?? '',
    item.caseType ?? '',
    item.productSolutionType ?? '',
    item.products ?? '',
    ukDate(item.createdOn),
    day(item.checkDate),
    item.preOrPostCheck ?? '',
    item.taxOutcome ?? '',
    item.initialOutcome ?? '',
    item.finalOutcome ?? '',
    item.latestOutcome ?? item.taxOutcome ?? '',
    ukDate(item.finalisedOn),
    // Tax runs from receipt; AQS from the hand-over, or from receipt on an AQS-only route.
    daysBetween(item.createdOn, item.taxSubmittedOn),
    daysBetween(item.taxSubmittedOn ?? item.createdOn, item.aqsSubmittedOn),
    // To the case's end, not today: the worklist's age keeps counting after a case finishes.
    daysBetween(item.createdOn, item.finalisedOn ?? item.aqsSubmittedOn ?? item.taxSubmittedOn),
    item.status,
  ];
}
