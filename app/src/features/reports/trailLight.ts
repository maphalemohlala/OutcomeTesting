import type { CellValue } from '../../lib/tabular';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';

/** The export record as the file builder needs it. */
export type ExportRecord = Al_exportrecords;

/**
 * The Trail Light contract fixed by AD-039 (source: `Trailight - Outcome Testing Map.xlsx`):
 * one row per case. Twenty columns in the supplied template's exact order, with column 16 an
 * intentional blank separator preserved so every downstream position matches. Do not add,
 * remove or reorder a column without a decision-log entry — the receiving system reads by
 * position.
 *
 * **Columns B and D carry CODES (project owner, 2026-09-22).** Trailight could not
 * accommodate the emails put there on 2026-09-21 (AD-183). The positions are unchanged, so
 * nothing downstream shifts; what changes is what B and D mean, which is a change a
 * positional reader cannot detect for itself.
 *
 * **What makes this workable now, when it was not before.** The codes used to be
 * `al_outcomecase.al_advisercode` / `al_paraplannercode`, filled only by hand and almost
 * never filled (OD-050) — which is why the columns were empty and why emails went in. The
 * code is now held against the PERSON, on `contact.al_staffcode`, and resolved at
 * generation time.
 *
 * **Six columns, not two.** The four fail-accountability code columns (L, N, R, T) were
 * blanked whenever a specific person was named accountable, because a contact carried no
 * code. They now carry that person's own.
 */
export const TRAIL_LIGHT_HEADERS = [
  'Adviser name',
  'Adviser Code',
  'Paraplanner Name',
  'Paraplanner Code',
  'Case type',
  'Product / solution type',
  'Check date',
  'Client name',
  'Pre or post check',
  'File Quality Grade',
  'File Quality Fail Accountable Adviser Name',
  'File Quality Fail Accountable Adviser Code',
  'File Quality Fail Accountable Paraplanner Name',
  'File Quality Fail Accountable Paraplanner Code',
  'Advice Quality Grade',
  '',
  'Advice Quality Fail Accountable Adviser Name',
  'Advice Quality Fail Accountable Adviser Code',
  'Advice Quality Fail Accountable Paraplanner Name',
  'Advice Quality Fail Accountable Paraplanner Code',
];

/**
 * AD-039 types the code columns as NUMBER. A code that is genuinely numeric is written as a
 * number so Excel does not left-pad or text-align it; anything else is passed through
 * unchanged rather than being silently dropped or coerced to zero.
 *
 * Six columns use it now, not four: columns B and D carry codes again as of 2026-09-22.
 */
function code(value: string | undefined): CellValue {
  const text = value?.trim();
  if (!text) return '';
  return /^-?\d+(\.\d+)?$/.test(text) ? Number(text) : text;
}

function text(value: string | undefined): string {
  return value?.trim() ?? '';
}

function day(value: string | undefined): string {
  return value ? value.slice(0, 10) : '';
}

export function trailLightRow(record: ExportRecord): CellValue[] {
  return [
    text(record.al_advisername),
    code(record.al_advisercode),
    text(record.al_paraplannername),
    // From `contact.al_staffcode`, resolved and snapshotted when the batch was generated
    // (GenerateExportPlugin.ParaplannerCode). Empty where the registry holds no code for
    // them — never the name or the address, which a positional reader could not tell apart
    // from a code.
    code(record.al_paraplannercode),
    text(record.al_casetype),
    text(record.al_productsolutiontype),
    day(record.al_checkdate),
    text(record.al_clientname),
    text(record.al_preorpostcheck),
    text(record.al_filequalitygrade),
    text(record.al_fqfailadvisername),
    code(record.al_fqfailadvisercode),
    text(record.al_fqfailparaplannername),
    code(record.al_fqfailparaplannercode),
    text(record.al_advicequalitygrade),
    text(record.al_separator),
    text(record.al_aqfailadvisername),
    code(record.al_aqfailadvisercode),
    text(record.al_aqfailparaplannername),
    code(record.al_aqfailparaplannercode),
  ];
}
