import type { CellValue } from '../../lib/tabular';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';
import { ukDayOf, ukToday } from '../cases/caseHeaderDates';
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
 * no filter, no "(1)". Two Trail Light downloads taken on one day are deliberately the same
 * name, because the convention describes the day's file rather than the click that made it;
 * a browser will suffix the second copy and the operator picks the one they meant.
 *
 * Underscores in the date, not the hyphens the rest of the app stamps with - `yyyy_mm_dd`
 * is what was given, and a convention is not improved by being made consistent with things
 * it is not part of.
 *
 * The day is the UK day. Users are in the UK and the browser may not be, and on a British
 * Summer Time evening a machine reading UTC is a day behind - which for a daily feed means
 * the file is named for yesterday. `ukToday` is the same helper the case header uses for
 * the same reason.
 */
export function trailLightFilename(extension: string, now: Date = new Date(), day?: string | null): string {
  // A daily file cut to one day is named for that day, so tomorrow's download of today's file
  // still arrives under today's name.
  return `DFALIN1_outcometesting_${(day || ukToday(now)).replace(/-/g, '_')}.${extension}`;
}

/**
 * The Trail Light contract fixed by AD-039 (source: `Trailight - Outcome Testing Map.xlsx`):
 * one row per case, in the supplied template's order. Do not add, remove or reorder a column
 * without a decision-log entry — the receiving system reads by position.
 *
 * **Nineteen columns, not twenty (AD-224, project owner, 2026-09-29).** The template's
 * column 16 was a blank separator, kept so every later position matched. It is removed:
 * the Advice Quality accountable pairs now sit in P to S, not Q to T.
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
 * **Six columns, not two.** The four fail-accountability code columns (L, N, Q, S) were
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
  'Advice Quality Fail Accountable Adviser Name',
  'Advice Quality Fail Accountable Adviser Code',
  'Advice Quality Fail Accountable Paraplanner Name',
  'Advice Quality Fail Accountable Paraplanner Code',
];

/**
 * AD-039 types the code columns as NUMBER, but a number cannot hold every code (owner,
 * 2026-11-17): "00123" would arrive as 123. So a code is written as a number only when that
 * number reads back as exactly the same text; leading zeros, signs, spaces, decimals such as
 * "1.50" and digit strings too long to hold exactly stay text, unchanged. CSV and XLSX alike.
 *
 * Six columns use it now, not four: columns B and D carry codes again as of 2026-09-22.
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

/**
 * Whether a row's check date (column G) falls in the range chosen for a download, inclusive
 * at both ends; an empty bound is open. Each batch snapshots every closed case and every
 * graded case not yet closed, so the range is how a download is cut to a period (2026-09-28).
 * A row with no check date cannot be shown to fall inside a range, so it is left out once one
 * is set.
 */
export function withinCheckDates(record: ExportRecord, from: string, to: string): boolean {
  if (!from && !to) return true;
  const checked = isoDay(record.al_checkdate);
  if (!checked) return false;
  if (from && checked < from) return false;
  if (to && checked > to) return false;
  return true;
}

/** Which Trail Light file is being built from a batch. */
export type TrailLightFile = 'daily' | 'quarterly';

/**
 * Whether the case was closed when the batch snapshotted it. A batch also holds graded cases
 * still open (owner, 2026-11-17), marked in the separator column the file no longer carries
 * (GenerateExportPlugin.NotYetClosedMarker); every batch before that holds closed cases only.
 * The quarterly file takes closed rows only.
 */
export function closedWhenSnapshotted(record: ExportRecord): boolean {
  return text(record.al_separator) === '';
}

/**
 * The check dates a file is cut to: the ones chosen on the page, else, for the daily file, the
 * UK days since the previous batch up to the day before this one. A batch holds what was checked
 * by the moment it was generated, so its own day is not over; cutting on it lost every case
 * checked later that day from every default file. Cut this way the batches tile the calendar:
 * each check day falls in exactly one batch's default file, a weekend without batches included.
 * `previousGeneratedOn` is the latest batch generated on an earlier UK day (none for the first).
 */
export function fileRange(
  file: TrailLightFile,
  from: string,
  to: string,
  generatedOn: string | null,
  previousGeneratedOn: string | null = null,
): { from: string; to: string } {
  const day = file === 'daily' && !from && !to && generatedOn ? ukDayOf(generatedOn) : null;
  if (!day) return { from, to };
  const last = dayBefore(day);
  const since = previousGeneratedOn ? ukDayOf(previousGeneratedOn) : null;
  return { from: since && since <= last ? since : last, to: last };
}

/** The calendar day before a yyyy-mm-dd day. */
export function dayBefore(isoDay: string): string {
  const [y, m, d] = isoDay.split('-').map(Number);
  return new Date(Date.UTC(y, m - 1, d - 1)).toISOString().slice(0, 10);
}

/** The latest batch generated on a UK day before this one's, which is where its default file starts. */
export function previousBatchGeneratedOn(
  batches: { id: string; generatedOn: string | null }[],
  batchId: string,
): string | null {
  const own = batches.find((b) => b.id === batchId)?.generatedOn;
  const ownDay = own ? ukDayOf(own) : null;
  if (!ownDay) return null;
  let best: string | null = null;
  for (const b of batches) {
    const day = b.generatedOn ? ukDayOf(b.generatedOn) : null;
    if (b.id !== batchId && day && day < ownDay && (!best || b.generatedOn! > best)) best = b.generatedOn;
  }
  return best;
}

const ADVICE_FAILS = ['insufficient evidence', 'potential harm'];
const NOT_A_PASS = ['pass with issues', 'fail', ...ADVICE_FAILS];

/**
 * The Advice Quality grade a file reports (client, 2026-11-16): the daily file the grade first
 * recorded, the quarterly file the one in force after any regrade.
 */
export function adviceGradeFor(record: ExportRecord, file: TrailLightFile): string {
  if (file === 'quarterly') return text(record.al_advicequalitygrade);
  // shortcut: a batch generated before 2026-11-16 holds no initial grade until
  // `backfillinitialgrade <org> --confirm <org>` (registration tool) is run in each environment;
  // until then such a row falls back to the grade in force. The fallback stays as the safety net.
  return text(record.al_initialadvicequalitygrade) || text(record.al_advicequalitygrade);
}

/**
 * Whether the row belongs in the daily file (client, 2026-11-16): any type of failure keeps it -
 * a File Quality Fail; an initial Advice Quality grade of Pass with issues, Insufficient
 * evidence or Potential harm (the Suitability Assessment grade); or a Tax-only check that came
 * back Fail or Pass with issues, which no Trail Light column carries. A clean pass is left out.
 * The quarterly 12-month file keeps every row.
 */
export function belongsInDailyFile(record: ExportRecord): boolean {
  return (
    text(record.al_filequalitygrade).toLowerCase() === 'fail' ||
    NOT_A_PASS.includes(adviceGradeFor(record, 'daily').toLowerCase()) ||
    NOT_A_PASS.includes(text(record.al_taxoutcome).toLowerCase())
  );
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

/**
 * The range a batch download is cut to, in words (2026-09-29). Users could not tell what the
 * two date boxes did, so the batch page says it in the dates they would write themselves.
 */
export function describeCheckRange(from: string, to: string): string {
  if (from && to) return from === to ? `checked on ${ukDay(from)}` : `checked ${ukDay(from)} to ${ukDay(to)}`;
  if (from) return `checked on or after ${ukDay(from)}`;
  if (to) return `checked on or before ${ukDay(to)}`;
  return '';
}

/** One sentence saying which of a batch's cases the file will hold. */
export function fileContentsSummary(
  inRange: number,
  total: number,
  from: string,
  to: string,
  leftOut = 0,
  file: TrailLightFile = 'daily',
): string {
  const range = describeCheckRange(from, to);
  if (leftOut > 0) {
    const held = inRange - leftOut;
    if (held === 0) {
      return `No case in this batch${range ? ` ${range}` : ''} belongs in the ${file} file, so it has nothing to download.`;
    }
    const left = leftOut === 1 ? '1 case that does not belong in it' : `${leftOut} cases that do not belong in it`;
    return `The file will contain ${held} of the ${total} cases in this batch${range ? `: those ${range}` : ''}, leaving out ${left}.`;
  }
  if (!range) {
    return total === 1
      ? 'The file will contain the 1 case in this batch.'
      : `The file will contain all ${total} cases in this batch.`;
  }
  if (inRange === 0) {
    return `None of the ${total} cases in this batch were ${range}, so there is nothing to download.`;
  }
  return `The file will contain ${inRange} of the ${total} cases in this batch: those ${range}.`;
}

export function trailLightRow(record: ExportRecord, file: TrailLightFile): CellValue[] {
  const advice = adviceGradeFor(record, file);
  // The batch keeps the advice-accountable people whenever either grade failed
  // (GenerateExportPlugin.AccountabilityGate); a file names them only beside its own fail.
  const aq = (value: string | undefined) => (ADVICE_FAILS.includes(advice.toLowerCase()) ? value : undefined);
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
    isoDay(record.al_checkdate),
    text(record.al_clientname),
    text(record.al_preorpostcheck),
    text(record.al_filequalitygrade),
    text(record.al_fqfailadvisername),
    code(record.al_fqfailadvisercode),
    text(record.al_fqfailparaplannername),
    code(record.al_fqfailparaplannercode),
    advice,
    text(aq(record.al_aqfailadvisername)),
    code(aq(record.al_aqfailadvisercode)),
    text(aq(record.al_aqfailparaplannername)),
    code(aq(record.al_aqfailparaplannercode)),
  ];
}

/** The one check day a daily file covers, or null when it is quarterly or spans several days. */
export function dailyFileDay(file: TrailLightFile, range: { from: string; to: string }): string | null {
  return file === 'daily' && range.from && range.from === range.to ? range.from : null;
}

/**
 * True when a daily file reaches the day its batch was generated, or later: cases checked after
 * the batch was generated that day are not in the batch, so the file may be short.
 */
export function dailyFileMayBeShort(
  file: TrailLightFile,
  range: { from: string; to: string },
  generatedOn: string | null,
): boolean {
  if (file !== 'daily' || !generatedOn) return false;
  const generatedDay = ukDayOf(generatedOn);
  return generatedDay !== null && (!range.to || range.to >= generatedDay);
}
