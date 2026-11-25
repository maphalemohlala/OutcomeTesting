import type { CellValue } from '../../lib/tabular';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import type { TrailLightFileKind } from '../../services/commands/exports';
import { ukToday } from '../cases/caseHeaderDates';
import { MONTHS, isoDay } from '../../lib/format';

/** The export record as the file builder needs it. */
export type ExportRecord = Al_exportrecords;

/**
 * What a Trail Light download is called: `DFALIN1_outcometesting_yyyy_mm_dd` (project
 * owner, 2026-09-23).
 *
 * **Part of the interface, not a nicety.** `DFALIN1` is the receiving end's name for this
 * feed, the same way the nineteen columns below are its shape - a file that arrives under
 * another name is a file nobody picks up. So this is used EXACTLY as given: no batch code,
 * no filter, no "(1)".
 *
 * Underscores in the date, not the hyphens the rest of the app stamps with - `yyyy_mm_dd`
 * is what was given.
 *
 * The day is the UK day: on a British Summer Time evening a machine reading UTC is a day
 * behind, which for a daily feed means the file is named for yesterday.
 */
export function trailLightFilename(extension: string, now: Date = new Date(), day?: string | null): string {
  // A daily file cut to one day is named for that day, so tomorrow's export of today's file
  // still arrives under today's name.
  return `DFALIN1_outcometesting_${(day || ukToday(now)).replace(/-/g, '_')}.${extension}`;
}

/**
 * The Trail Light contract fixed by AD-039 (source: `Trailight - Outcome Testing Map.xlsx`):
 * one row per case, in the supplied template's order. Do not add, remove or reorder a column
 * without a decision-log entry — the receiving system reads by position.
 *
 * **Nineteen columns, not twenty (AD-224).** The template's blank separator is removed.
 *
 * **Columns B and D carry CODES (project owner, 2026-09-22)**, held against the person on
 * `contact.al_staffcode` and resolved when the batch is generated.
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
  'Advice Quality Fail Accountable Adviser Name',
  'Advice Quality Fail Accountable Adviser Code',
  'Advice Quality Fail Accountable Paraplanner Name',
  'Advice Quality Fail Accountable Paraplanner Code',
];

/**
 * AD-039 types the code columns as NUMBER, but a number cannot hold every code (owner,
 * 2026-11-17): "00123" would arrive as 123. So a code is written as a number only when that
 * number reads back as exactly the same text; anything else stays text. CSV and XLSX alike.
 */
function code(value: string | undefined): CellValue {
  const text = value?.trim();
  if (!text) return '';
  const asNumber = Number(text);
  return Number.isFinite(asNumber) && String(asNumber) === text ? asNumber : text;
}

function text(value: string | undefined): string {
  return value?.trim() ?? '';
}

/** The calendar day before a yyyy-mm-dd day. */
export function dayBefore(isoDay: string): string {
  const [y, m, d] = isoDay.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d - 1)).toISOString().slice(0, 10);
}

/** The one check day a daily file covers, which names it; null when quarterly or several days. */
export function dailyFileDay(file: TrailLightFileKind, from: string, to: string): string | null {
  return file === 'Daily' && from && from === to ? from : null;
}

/**
 * The calendar quarter a day falls in, as the two inclusive ISO days of its range, and a
 * label. Quarters are calendar quarters (Jan-Mar, Apr-Jun, Jul-Sep, Oct-Dec).
 */
export function quarterOf(isoDay: string): { from: string; to: string; label: string } {
  const [year, month] = isoDay.split('-').map(Number);
  const q = Math.floor((month - 1) / 3);
  const first = q * 3 + 1;
  const last = first + 2;
  const lastDay = new Date(Date.UTC(year, last, 0)).getUTCDate();
  const pad = (n: number) => String(n).padStart(2, '0');
  return {
    from: `${year}-${pad(first)}-01`,
    to: `${year}-${pad(last)}-${pad(lastDay)}`,
    label: `Q${q + 1} ${year} (${MONTHS[first - 1]} to ${MONTHS[last - 1]})`,
  };
}

/** The current quarter and the ones before it, newest first. */
export function recentQuarters(today: string, count = 8): { from: string; to: string; label: string }[] {
  const quarters = [quarterOf(today)];
  while (quarters.length < count) {
    const [y, m] = quarters[quarters.length - 1].from.split('-').map(Number);
    quarters.push(quarterOf(m === 1 ? `${y - 1}-10-01` : `${y}-${String(m - 3).padStart(2, '0')}-01`));
  }
  return quarters;
}

/** `2026-09-28` as `28 Sep 2026`, read from the digits so no time zone can move the day. */
export function ukDay(isoDay: string): string {
  const [year, month, dayOfMonth] = isoDay.split('-').map(Number);
  return `${dayOfMonth} ${MONTHS[month - 1]} ${year}`;
}

/** A check-date range in the words a user would write; empty when no date is set. */
export function describeCheckRange(from: string, to: string): string {
  if (from && to) return from === to ? `checked on ${ukDay(from)}` : `checked ${ukDay(from)} to ${ukDay(to)}`;
  if (from) return `checked on or after ${ukDay(from)}`;
  if (to) return `checked on or before ${ukDay(to)}`;
  return '';
}

/**
 * One file row. The batch holds only its file's rows, already with the grade that file reports
 * and the accountable people blank where it reports no fail (GenerateExportPlugin), so this
 * only puts the snapshot in column order.
 */
export function trailLightRow(record: ExportRecord): CellValue[] {
  return [
    text(record.al_advisername),
    code(record.al_advisercode),
    text(record.al_paraplannername),
    // Empty where the registry holds no code — never the name or the address, which a
    // positional reader could not tell apart from a code.
    code(record.al_paraplannercode),
    text(record.al_casetype),
    text(record.al_productsolutiontype),
    isoDay(record.al_checkdate),
    text(record.al_clientname),
    text(record.al_preorpostcheck),
    text(record.al_filequalitygrade),
    text(record.al_fqfailadvisername),
    code(record.al_fqfailadvisercode),
    text(record.al_fqfailparaplannername),
    code(record.al_fqfailparaplannercode),
    text(record.al_advicequalitygrade),
    text(record.al_aqfailadvisername),
    code(record.al_aqfailadvisercode),
    text(record.al_aqfailparaplannername),
    code(record.al_aqfailparaplannercode),
  ];
}
