import type { CellValue } from '../../lib/tabular';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';

/**
 * The export record as the file builder needs it.
 *
 * `al_adviseremail` is on the generated model: that column was created in DEV on 2026-09-19
 * and the data source regenerated. `al_paraplanneremail` is declared HERE because it was
 * created on 2026-09-21 and the generator has not been re-run against it yet — the same
 * situation AD-158 records for a Custom API parameter, where the runtime accepted a value
 * the generated types did not yet know about.
 *
 * Declared as an intersection rather than edited into `Al_exportrecordsModel.ts`, because
 * that file is generated and a hand-edit is lost the next time anyone regenerates it.
 * `paraplannerEmailIsGenerated` below is the guard: it goes true of its own accord once the
 * generator catches up, and its test says to delete this intersection when it does.
 */
export type ExportRecord = Al_exportrecords & { al_paraplanneremail?: string };

/**
 * Whether the generated model has caught up and declares `al_paraplanneremail` itself.
 *
 * A type-level check, not a runtime one: an interface has no runtime presence, so asking a
 * value whether it has the key would answer no for ever and guard nothing.
 *
 * It reads `false` today. The moment a regeneration adds the column, this alias becomes
 * `true` and the assignment below stops compiling — which is the point. The typecheck then
 * says, in the one place that knows, that the hand-declared intersection above is redundant
 * and both it and this guard should be deleted.
 */
type GeneratedHasParaplannerEmail = 'al_paraplanneremail' extends keyof Al_exportrecords
  ? true
  : false;

export const paraplannerEmailIsGenerated: GeneratedHasParaplannerEmail = false;

/**
 * The Trail Light contract fixed by AD-039 (source: `Trailight - Outcome Testing Map.xlsx`):
 * one row per case. Twenty columns in the supplied template's exact order, with column 16 an
 * intentional blank separator preserved so every downstream position matches. Do not add,
 * remove or reorder a column without a decision-log entry — the receiving system reads by
 * position.
 *
 * **Columns B and D carry emails, not codes (project owner, 2026-09-21: "replace column B &
 * D on the trail light exports with emails. rather than codes show emails").** The positions
 * are unchanged, so nothing downstream shifts; what changes is what column B and column D
 * mean, which is a change a positional reader cannot detect for itself. That is why it is
 * written here and in the decision log rather than only in the code.
 *
 * **Column 21 is gone with it.** Adviser Email was appended there on 2026-09-19 precisely
 * because inserting it beside the adviser's name would have shifted eighteen columns; now
 * that column B carries it, keeping 21 would only repeat the value. Removing the LAST column
 * is as position-safe as adding it was — nothing before it moves — and the project owner
 * chose that over the duplicate on 2026-09-21.
 *
 * The four fail-accountable code columns (L, N, R, T) are deliberately unchanged. Only B and
 * D were asked for, and those four name a person picked out of a fail rather than the case's
 * own adviser and para-planner.
 */
export const TRAIL_LIGHT_HEADERS = [
  'Adviser name',
  'Adviser Email',
  'Paraplanner Name',
  'Paraplanner Email',
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
 * Four columns still use it, not six: columns B and D became emails on 2026-09-21 and an
 * email is text whatever it looks like.
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
    text(record.al_adviseremail),
    text(record.al_paraplannername),
    // Resolved from the para-planner's name when the export batch was generated, not read
    // off the case: the case has no para-planner email column. GenerateExportPlugin refuses
    // to guess between two contacts of one name, so this is empty where the name was
    // ambiguous or reached nobody.
    text(record.al_paraplanneremail),
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
