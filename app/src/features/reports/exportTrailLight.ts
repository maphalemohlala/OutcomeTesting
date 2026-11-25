import { generateExport, type TrailLightFileKind } from '../../services/commands/exports';
import { messageForFailure } from '../../services/errors';
import { downloadCsv, downloadWorkbook } from '../../lib/tabular';
import { loadExportRecords } from './useExports';
import { TRAIL_LIGHT_HEADERS, dailyFileDay, describeCheckRange, trailLightFilename, trailLightRow } from './trailLight';

export interface TrailLightChoice {
  file: TrailLightFileKind;
  /** yyyy-mm-dd, both ends included; empty for an open bound. */
  from: string;
  to: string;
  format: 'xlsx' | 'csv';
  /** The intent's idempotency key, which is also the new batch's id. */
  key: string;
}

export interface TrailLightOutcome {
  tone: 'ok' | 'error';
  message: string;
  /** A batch now exists, so the list of previous exports is stale. */
  produced: boolean;
  /** The intent is over; false keeps the key, so trying again re-reads the same batch. */
  settled: boolean;
}

/**
 * The whole Trail Light export: produce the batch for the choice, read its rows back and
 * download them. Nothing is downloaded unless every row the batch recorded was read.
 */
export async function exportTrailLight(choice: TrailLightChoice): Promise<TrailLightOutcome> {
  const { file, from, to, format, key } = choice;
  const range = describeCheckRange(from, to);
  const result = await generateExport({ batchId: key, idempotencyKey: key, fileKind: file, checkedFrom: from, checkedTo: to });
  if (!result.ok) return { tone: 'error', message: messageForFailure(result), produced: false, settled: false };

  const rowCount = Number(result.data.RowCount ?? 0);
  if (rowCount === 0) {
    return {
      tone: 'error',
      message: `No case ${range ? `${range} ` : ''}belongs in the ${file.toLowerCase()} file, so there is nothing to download.`,
      produced: true,
      settled: true,
    };
  }

  const records = await loadExportRecords(result.data.BatchId || key);
  if (records === null || records.length !== rowCount) {
    return {
      tone: 'error',
      message: `The file holds ${rowCount} case(s) but ${records?.length ?? 0} could be read, so it was not downloaded. Select Export again to retry.`,
      produced: true,
      settled: false,
    };
  }

  const rows = records.map(trailLightRow);
  const name = trailLightFilename(format, undefined, dailyFileDay(file, from, to));
  if (format === 'xlsx') downloadWorkbook(name, [{ name: 'Trail Light', headers: TRAIL_LIGHT_HEADERS, rows }]);
  else downloadCsv(name, TRAIL_LIGHT_HEADERS, rows);
  return {
    tone: 'ok',
    message: `Downloaded ${name}: ${rowCount} case(s)${range ? `, ${range}` : ''}.`,
    produced: true,
    settled: true,
  };
}
