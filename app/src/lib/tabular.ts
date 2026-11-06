/*
 * Shared download helpers for the reporting and export screens. Building the file in the
 * browser from data Dataverse has already returned keeps row visibility intact: a user can
 * only export what they can read (BR-012).
 */

import { ukToday } from '../features/cases/caseHeaderDates';
import { buildWorkbook, type CellValue, type Sheet } from './xlsx';

export type { CellValue, Sheet } from './xlsx';

/** Quotes a cell that carries a delimiter, quote or newline, and changes nothing else. */
export function quoteCsv(text: string): string {
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

/** Excel runs a text cell starting with these as a formula when it opens a CSV. */
const FORMULA_START = /^[=+\-@\t\r]/;

function csvCell(value: CellValue): string {
  if (value === null || value === undefined) return '';
  if (typeof value !== 'string') return quoteCsv(String(value));
  return quoteCsv(FORMULA_START.test(value) ? `'${value}` : value);
}

export function buildCsv(headers: string[], rows: CellValue[][]): string {
  const lines = [headers.map(csvCell).join(','), ...rows.map((row) => row.map(csvCell).join(','))];
  return `${lines.join('\r\n')}\r\n`;
}

function download(filename: string, blob: Blob): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

/** UTF-8 BOM so Excel reads accented client and adviser names correctly. */
export function downloadCsv(filename: string, headers: string[], rows: CellValue[][]): void {
  download(filename, new Blob(['\uFEFF', buildCsv(headers, rows)], { type: 'text/csv;charset=utf-8;' }));
}

export function downloadWorkbook(filename: string, sheets: Sheet[]): void {
  const bytes = buildWorkbook(sheets);
  const blob = new Blob([bytes as unknown as BlobPart], {
    type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
  });
  download(filename, blob);
}

/** `outcome-cases-2026-09-03.xlsx` — dated so successive extracts do not overwrite. */
export function stampedFilename(stem: string, extension: string): string {
  return `${stem}-${ukToday()}.${extension}`;
}
