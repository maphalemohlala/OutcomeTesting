import type { CellValue } from '../../lib/tabular';
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
  'Owner',
  'Client name',
  'Adviser name',
  'Adviser code',
  'Paraplanner name',
  'Paraplanner code',
  'Checker name',
  'Case type',
  'Product / solution type',
  'Products',
  'Advice date',
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
    item.checker ?? '',
    item.caseType ?? '',
    item.productSolutionType ?? '',
    item.products ?? '',
    day(item.adviceDate),
    day(item.checkDate),
    item.preOrPostCheck ?? '',
    day(item.createdOn),
    day(item.dueDate),
    item.ageInDays,
    item.initialOutcome ?? '',
    item.finalOutcome ?? '',
    day(item.finalisedOn),
  ];
}

/**
 * The completed-case report: closed cases with the product and the outcome they closed
 * on. Narrower than the full extract on purpose — it answers "what did we grade, and on
 * what", so allocation and ageing columns are left out (BR-010).
 */
export const COMPLETED_CASE_HEADERS = [
  'Case reference',
  'Client name',
  'Adviser name',
  'Adviser code',
  'Paraplanner name',
  'Paraplanner code',
  'Case type',
  'Product / solution type',
  'Products',
  'Check date',
  'Pre or post check',
  'Initial outcome',
  'Final outcome',
  'Outcome',
  'Finalised on',
  'Status',
];

export function completedCaseRow(item: CaseSummary): CellValue[] {
  return [
    item.caseReference,
    item.client ?? '',
    item.adviser ?? '',
    item.adviserCode ?? '',
    item.paraplanner ?? '',
    item.paraplannerCode ?? '',
    item.caseType ?? '',
    item.productSolutionType ?? '',
    item.products ?? '',
    day(item.checkDate),
    item.preOrPostCheck ?? '',
    item.initialOutcome ?? '',
    item.finalOutcome ?? '',
    item.latestOutcome ?? '',
    day(item.finalisedOn),
    item.status,
  ];
}
