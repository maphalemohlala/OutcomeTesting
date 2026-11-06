/**
 * A quick look at an extract before it is sent, and the report of what the server refused.
 *
 * Every rule about a row lives in `ImportRules` on the server (AD-003). This only saves a round
 * trip on a file that is plainly not an extract, so it holds no second copy of those rules.
 */

import { buildCsv, downloadCsv, type CellValue } from '../../lib/tabular';

/** One row per rejected/skipped case, so a user can correct and re-upload (FR-002). */
export interface ValidationReportRow {
  rowNumber: number;
  caseReference: string | null;
  status: string;
  reason: string;
  raw: string;
}

const REPORT_HEADERS = ['Row', 'TaskID', 'Status', 'Reason', 'Original row'];

function reportCells(r: ValidationReportRow): CellValue[] {
  return [r.rowNumber, r.caseReference ?? '', r.status, r.reason, r.raw];
}

export function buildValidationReportCsv(rows: ValidationReportRow[]): string {
  return buildCsv(REPORT_HEADERS, rows.map(reportCells));
}

export function downloadValidationReport(
  rows: ValidationReportRow[],
  batchReference: string,
): void {
  downloadCsv(`${batchReference}-validation-report.csv`, REPORT_HEADERS, rows.map(reportCells));
}

/** Tokenise CSV text into rows of fields, honouring quotes and embedded newlines. */
function tokenise(text: string): string[][] {
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = '';
  let inQuotes = false;

  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i];
    if (inQuotes) {
      if (ch === '"') {
        if (text[i + 1] === '"') {
          cell += '"';
          i += 1;
        } else {
          inQuotes = false;
        }
      } else {
        cell += ch;
      }
      continue;
    }
    if (ch === '"') {
      inQuotes = true;
    } else if (ch === ',') {
      row.push(cell);
      cell = '';
    } else if (ch === '\n') {
      row.push(cell);
      rows.push(row);
      row = [];
      cell = '';
    } else if (ch !== '\r') {
      cell += ch;
    }
  }
  if (cell !== '' || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }
  return rows;
}

/** Why this file is plainly not an extract, or null when it is worth sending. */
export function checkExtract(text: string): string | null {
  const rows = tokenise(text).filter((row) => row.some((cell) => cell.trim() !== ''));
  if (rows.length === 0) return 'The file is empty.';

  if (!rows[0].some((header) => header.trim().toLowerCase() === 'taskid')) {
    return 'The file is missing the "TaskID" column. Use the Intelligent Office task extract.';
  }
  return rows.length === 1 ? 'The file has no case rows to import.' : null;
}

/** A CSV's text. Excel's plain "CSV" save is Windows-1252, and read as UTF-8 that garbles names. */
export function decodeCsv(bytes: ArrayBuffer): string {
  try {
    return new TextDecoder('utf-8', { fatal: true }).decode(bytes);
  } catch {
    return new TextDecoder('windows-1252').decode(bytes);
  }
}
