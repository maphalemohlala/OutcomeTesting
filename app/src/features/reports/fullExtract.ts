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
import { readAll } from '../../services/readAll';

/** Sheet names are the business names a manager recognises, not the table logical names. */
const SOURCES: { name: string; read: () => Promise<{ success: boolean; data?: unknown }> }[] = [
  { name: 'Cases', read: () => readAll((o) => Al_outcomecasesService.getAll(o), { orderBy: ['al_outcomecaseid asc'] }) },
  { name: 'Reviews', read: () => readAll((o) => Al_reviewinstancesService.getAll(o), { orderBy: ['al_reviewinstanceid asc'] }) },
  { name: 'Responses', read: () => readAll((o) => Al_responsesService.getAll(o), { orderBy: ['al_responseid asc'] }) },
  { name: 'Outcomes', read: () => readAll((o) => Al_outcomesService.getAll(o), { orderBy: ['al_outcomeid asc'] }) },
  { name: 'Remediation actions', read: () => readAll((o) => Al_remediationactionsService.getAll(o), { orderBy: ['al_remediationactionid asc'] }) },
  { name: 'Sign-offs', read: () => readAll((o) => Al_signoffsService.getAll(o), { orderBy: ['al_signoffid asc'] }) },
  { name: 'Import batches', read: () => readAll((o) => Al_importbatchesService.getAll(o), { orderBy: ['al_importbatchid asc'] }) },
  { name: 'Import exceptions', read: () => readAll((o) => Al_importexceptionsService.getAll(o), { orderBy: ['al_importexceptionid asc'] }) },
  { name: 'Export batches', read: () => readAll((o) => Al_exportbatchesService.getAll(o), { orderBy: ['al_exportbatchid asc'] }) },
  { name: 'Export records', read: () => readAll((o) => Al_exportrecordsService.getAll(o), { orderBy: ['al_exportrecordid asc'] }) },
  { name: 'Audit events', read: () => readAll((o) => Al_auditeventsService.getAll(o), { orderBy: ['al_auditeventid asc'] }) },
  { name: 'Sections', read: () => readAll((o) => Al_sectionsService.getAll(o), { orderBy: ['al_sectionid asc'] }) },
  { name: 'Questions', read: () => readAll((o) => Al_questionsService.getAll(o), { orderBy: ['al_questionid asc'] }) },
  { name: 'Question versions', read: () => readAll((o) => Al_questionversionsService.getAll(o), { orderBy: ['al_questionversionid asc'] }) },
  { name: 'People', read: () => readAll((o) => ContactsService.getAll(o), { orderBy: ['contactid asc'] }) },
  // The registry is the Power Pages web roles (AD-087). This read al_role, which has
  // held the retired vocabulary since - eleven rows nothing resolves against - so the
  // sheet was reporting roles nobody can hold as though they were the role list.
  { name: 'Roles', read: () => Mspp_webrolesService.getAll() },
  { name: 'Role assignments', read: () => readAll((o) => Al_userrolemappingsService.getAll(o), { orderBy: ['al_userrolemappingid asc'] }) },
  { name: 'Permission rules', read: () => readAll((o) => Al_pagepermissionsService.getAll(o), { orderBy: ['al_pagepermissionid asc'] }) },
];

export interface FullExtract {
  sheets: Sheet[];
  /** Tables the caller may not read, or that failed. Named so the file is never silently short. */
  unavailable: string[];
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

  for (const { source, result } of results) {
    if (!result.success || !Array.isArray(result.data)) {
      unavailable.push(source.name);
      continue;
    }
    sheets.push(toSheet(source.name, result.data as Record<string, unknown>[]));
  }

  return { sheets, unavailable };
}
