import {
  Al_auditeventsService,
  Al_exportbatchesService,
  Al_exportrecordsService,
  Al_importbatchesService,
  Al_importexceptionsService,
  Al_outcomecasesService,
  Al_outcomesService,
  Al_pagepermissionsService,
  Al_questionsService,
  Al_questionversionsService,
  Al_remediationactionsService,
  Al_responsesService,
  Al_reviewinstancesService,
  Al_sectionsService,
  Al_signoffsService,
  Al_userrolemappingsService,
  ContactsService,
  Mspp_webrolesService,
} from '../../generated';
import type { Sheet } from '../../lib/tabular';
// Shaping a sheet lives in extractSheet so it can be tested: this module imports the
// generated Services, which drag in the Power Apps SDK and will not load under vitest.
import { toSheet } from './extractSheet';

/** Per-table ceiling. A truncated sheet is reported rather than passed off as complete. */
export const EXTRACT_ROW_LIMIT = 5000;

/** Sheet names are the business names a manager recognises, not the table logical names. */
const SOURCES: { name: string; read: () => Promise<{ success: boolean; data?: unknown }> }[] = [
  { name: 'Cases', read: () => Al_outcomecasesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Reviews', read: () => Al_reviewinstancesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Responses', read: () => Al_responsesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Outcomes', read: () => Al_outcomesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Remediation actions', read: () => Al_remediationactionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Sign-offs', read: () => Al_signoffsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Import batches', read: () => Al_importbatchesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Import exceptions', read: () => Al_importexceptionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Export batches', read: () => Al_exportbatchesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Export records', read: () => Al_exportrecordsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Audit events', read: () => Al_auditeventsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Sections', read: () => Al_sectionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Questions', read: () => Al_questionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Question versions', read: () => Al_questionversionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'People', read: () => ContactsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  // The registry is the Power Pages web roles (AD-087). This read al_role, which has
  // held the retired vocabulary since - eleven rows nothing resolves against - so the
  // sheet was reporting roles nobody can hold as though they were the role list.
  { name: 'Roles', read: () => Mspp_webrolesService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Role assignments', read: () => Al_userrolemappingsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
  { name: 'Permission rules', read: () => Al_pagepermissionsService.getAll({ top: EXTRACT_ROW_LIMIT }) },
];

export interface FullExtract {
  sheets: Sheet[];
  /** Tables the caller may not read, or that failed. Named so the file is never silently short. */
  unavailable: string[];
  /** Tables that hit the row ceiling and are therefore incomplete. */
  truncated: string[];
}

/**
 * Reads every table the signed-in user is permitted to see into one workbook. Dataverse
 * enforces row visibility (BR-012), so this cannot widen anyone's access: a table the
 * caller cannot read comes back as unavailable rather than as an empty sheet implying
 * there is nothing there.
 */
export async function buildFullExtract(): Promise<FullExtract> {
  const results = await Promise.all(
    SOURCES.map(async (source) => {
      try {
        const result = await source.read();
        return { source, result };
      } catch {
        return { source, result: { success: false } };
      }
    }),
  );

  const sheets: Sheet[] = [];
  const unavailable: string[] = [];
  const truncated: string[] = [];

  for (const { source, result } of results) {
    if (!result.success || !Array.isArray(result.data)) {
      unavailable.push(source.name);
      continue;
    }
    const records = result.data as Record<string, unknown>[];
    if (records.length >= EXTRACT_ROW_LIMIT) truncated.push(source.name);
    sheets.push(toSheet(source.name, records));
  }

  return { sheets, unavailable, truncated };
}
