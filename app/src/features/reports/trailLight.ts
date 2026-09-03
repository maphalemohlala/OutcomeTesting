import type { CellValue } from '../../lib/tabular';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';

/**
 * The Trail Light contract fixed by AD-039 (source: `Trailight - Outcome Testing Map.xlsx`):
 * one row per case, twenty columns in this exact order. Column 16 is an intentional blank
 * separator in the supplied template and is preserved so every downstream column position
 * matches. Do not add, remove or reorder a column here without a decision-log entry — the
 * receiving system reads by position.
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
 * AD-039 types the four code columns as NUMBER. A code that is genuinely numeric is written
 * as a number so Excel does not left-pad or text-align it; anything else is passed through
 * unchanged rather than being silently dropped or coerced to zero.
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

export function trailLightRow(record: Al_exportrecords): CellValue[] {
  return [
    text(record.al_advisername),
    code(record.al_advisercode),
    text(record.al_paraplannername),
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
