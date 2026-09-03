import {
  Al_outcomecasesal_adviserstatus,
  Al_outcomecasesal_casetype,
  Al_outcomecasesal_preorpostcheck,
  Al_outcomecasesal_productsolutiontype,
  Al_outcomecasesal_samplesource,
  Al_outcomecasesal_taxcheckrequired,
  Al_outcomecasesal_taxteamdisposition,
  Al_outcomecasesal_vulnerableclient,
  type Al_outcomecasesBase,
} from '../../generated/models/Al_outcomecasesModel';

/** al_casestatus for a freshly imported case (Imported, BR-001). */
const CASE_STATUS_IMPORTED = 120910580;

type ColumnKind = 'text' | 'date' | 'choice';

interface ColumnDef {
  /** Header text as it appears in the template CSV. */
  header: string;
  /** Target al_outcomecase field. */
  field: keyof Al_outcomecasesBase;
  kind: ColumnKind;
  required?: boolean;
  /** Numeric option-set map (label -> value) for choice columns. */
  choices?: Record<number, string>;
  /** Example value shown in the template's guide row. */
  example: string;
}

/**
 * Case-header columns from knowledge/checklist-v8.md, mapped to the deployed
 * al_outcomecase schema. IO reference is the BR-001 import key.
 */
const COLUMNS: ColumnDef[] = [
  { header: 'IO reference', field: 'al_casereference', kind: 'text', required: true, example: 'IO-000123' },
  { header: 'Client name', field: 'al_clientname', kind: 'text', example: 'A. Client' },
  { header: 'Adviser name', field: 'al_advisername', kind: 'text', example: 'Jane Adviser' },
  { header: 'Adviser code', field: 'al_advisercode', kind: 'text', example: 'ADV-01' },
  {
    header: 'Adviser status',
    field: 'al_adviserstatus',
    kind: 'choice',
    choices: Al_outcomecasesal_adviserstatus,
    example: 'CAS',
  },
  { header: 'Paraplanner', field: 'al_paraplanner', kind: 'text', example: 'Sam Paraplanner' },
  { header: 'Paraplanner code', field: 'al_paraplannercode', kind: 'text', example: 'PP-01' },
  { header: 'Products', field: 'al_products', kind: 'text', example: 'Pension; ISA' },
  {
    header: 'Case type',
    field: 'al_casetype',
    kind: 'choice',
    choices: Al_outcomecasesal_casetype,
    example: 'New advice',
  },
  { header: 'Advice date', field: 'al_advicedate', kind: 'date', example: '31/01/2026' },
  {
    header: 'Product / solution type',
    field: 'al_productsolutiontype',
    kind: 'choice',
    choices: Al_outcomecasesal_productsolutiontype,
    example: 'Accumulation Pension',
  },
  {
    header: 'Sample source',
    field: 'al_samplesource',
    kind: 'choice',
    choices: Al_outcomecasesal_samplesource,
    example: 'Random',
  },
  { header: 'Checker name', field: 'al_checkername', kind: 'text', example: 'Chris Checker' },
  { header: 'Check date', field: 'al_checkdate', kind: 'date', example: '05/02/2026' },
  {
    header: 'Pre or post check',
    field: 'al_preorpostcheck',
    kind: 'choice',
    choices: Al_outcomecasesal_preorpostcheck,
    example: 'Pre',
  },
  {
    header: 'Vulnerable client',
    field: 'al_vulnerableclient',
    kind: 'choice',
    choices: Al_outcomecasesal_vulnerableclient,
    example: 'N/A',
  },
  {
    header: 'Tax check required',
    field: 'al_taxcheckrequired',
    kind: 'choice',
    choices: Al_outcomecasesal_taxcheckrequired,
    example: 'No',
  },
  {
    header: 'Tax team disposition',
    field: 'al_taxteamdisposition',
    kind: 'choice',
    choices: Al_outcomecasesal_taxteamdisposition,
    example: 'Submit to AQS',
  },
];

export const TEMPLATE_HEADERS = COLUMNS.map((c) => c.header);

export interface ParsedCase {
  rowNumber: number;
  reference: string;
  record: Omit<Al_outcomecasesBase, 'al_outcomecaseid'>;
  raw: string;
}

export interface RowError {
  rowNumber: number;
  caseReference: string | null;
  reason: string;
  raw: string;
}

export interface ParseResult {
  valid: ParsedCase[];
  invalid: RowError[];
  /** Set when the file has no usable header row. */
  fatal: string | null;
}

function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

/** Header row plus one guide row so users can see the expected format. */
export function buildTemplateCsv(): string {
  const header = TEMPLATE_HEADERS.map(csvCell).join(',');
  // Choice columns list every accepted value so uploads use options the schema knows;
  // this guide row is skipped on import (isGuideRow), so it never becomes a case.
  const guide = COLUMNS.map((c) =>
    csvCell(c.kind === 'choice' && c.choices ? Object.values(c.choices).join(' | ') : c.example),
  ).join(',');
  return `${header}\r\n${guide}\r\n`;
}

function downloadCsv(filename: string, content: string): void {
  const blob = new Blob([content], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export function downloadTemplate(): void {
  downloadCsv('outcome-case-upload-template.csv', buildTemplateCsv());
}

/** One row per rejected/skipped case, so a user can correct and re-upload (FR-002). */
export interface ValidationReportRow {
  rowNumber: number;
  caseReference: string | null;
  status: string;
  reason: string;
  raw: string;
}

export function buildValidationReportCsv(rows: ValidationReportRow[]): string {
  const header = ['Row', 'IO reference', 'Status', 'Reason', 'Original row']
    .map(csvCell)
    .join(',');
  const body = rows
    .map((r) =>
      [String(r.rowNumber), r.caseReference ?? '', r.status, r.reason, r.raw]
        .map(csvCell)
        .join(','),
    )
    .join('\r\n');
  return `${header}\r\n${body}\r\n`;
}

export function downloadValidationReport(
  rows: ValidationReportRow[],
  batchReference: string,
): void {
  downloadCsv(`${batchReference}-validation-report.csv`, buildValidationReportCsv(rows));
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

function findChoice(map: Record<number, string>, label: string): number | null {
  const needle = label.trim().toLowerCase();
  for (const [value, text] of Object.entries(map)) {
    if (text.toLowerCase() === needle) return Number(value);
  }
  // Also accept the raw numeric option value, so extracts that carry codes still import.
  if (/^\d+$/.test(needle) && Object.prototype.hasOwnProperty.call(map, needle)) {
    return Number(needle);
  }
  return null;
}

/**
 * Accepts dd/mm/yyyy (UK) or yyyy-mm-dd, and written-out forms like "31 Jan 2026";
 * returns yyyy-mm-dd. Kept deliberately in step with `ImportRules.ParseDate` in the
 * plug-in, which is the rule this only previews — a value the two disagree about would
 * show as importable here and then be rejected by the command.
 *
 * A numeric date that matches neither accepted order is rejected rather than handed to
 * `new Date`, which reads month-first: 01/13/2026 would come back as 13 January, and an
 * extract that named a thirteenth month is a data error, not a January date.
 */
function parseDate(value: string): string | null {
  const trimmed = value.trim();

  const uk = /^(\d{1,2})\/(\d{1,2})\/(\d{4})$/.exec(trimmed);
  if (uk) {
    const [, d, m, y] = uk;
    return isoIfReal(Number(y), Number(m), Number(d));
  }

  const iso = /^(\d{4})-(\d{1,2})-(\d{1,2})(?:[T\s].*)?$/.exec(trimmed);
  if (iso) {
    return isoIfReal(Number(iso[1]), Number(iso[2]), Number(iso[3]));
  }

  if (/^[\d./-]+$/.test(trimmed)) return null;

  const parsed = new Date(trimmed);
  if (Number.isNaN(parsed.getTime())) return null;
  // Read back the local components, not the UTC ones. `new Date('31 Jan 2026')` is local
  // midnight, and `toISOString()` on that lands on the 30th anywhere east of UTC — an
  // advice date silently a day early, which no later check would catch.
  return isoIfReal(parsed.getFullYear(), parsed.getMonth() + 1, parsed.getDate());
}

/** Formats a date, rejecting one that does not exist (31 February and the like). */
function isoIfReal(year: number, month: number, day: number): string | null {
  const date = new Date(Date.UTC(year, month - 1, day));
  if (
    date.getUTCFullYear() !== year ||
    date.getUTCMonth() !== month - 1 ||
    date.getUTCDate() !== day
  ) {
    return null;
  }
  return `${String(year).padStart(4, '0')}-${String(month).padStart(2, '0')}-${String(day).padStart(2, '0')}`;
}

function isGuideRow(fields: string[]): boolean {
  const ref = (fields[0] ?? '').trim().toLowerCase();
  return ref === 'io-000123';
}

/**
 * Parses an Intelligent Office extract into cases to create and rows to flag.
 * Only IO reference is mandatory (BR-001); other columns are validated only when
 * present. No business rule is invented — unrecognised choices become exceptions.
 */
export function parseCaseCsv(text: string): ParseResult {
  const rows = tokenise(text).filter((r) => r.some((c) => c.trim() !== ''));
  if (rows.length === 0) {
    return { valid: [], invalid: [], fatal: 'The file is empty.' };
  }

  const headerRow = rows[0].map((h) => h.trim().toLowerCase());
  const columnIndex = new Map<keyof Al_outcomecasesBase, number>();
  for (const col of COLUMNS) {
    const idx = headerRow.indexOf(col.header.toLowerCase());
    if (idx !== -1) columnIndex.set(col.field, idx);
  }
  if (!columnIndex.has('al_casereference')) {
    return {
      valid: [],
      invalid: [],
      fatal: 'The file is missing the "IO reference" column. Use the supplied template.',
    };
  }

  const valid: ParsedCase[] = [];
  const invalid: RowError[] = [];
  const seen = new Set<string>();

  for (let r = 1; r < rows.length; r += 1) {
    const fields = rows[r];
    if (isGuideRow(fields)) continue;
    const rowNumber = r + 1;
    const raw = fields.map(csvCell).join(',').slice(0, 2000);
    const cellOf = (field: keyof Al_outcomecasesBase): string =>
      (fields[columnIndex.get(field) ?? -1] ?? '').trim();

    const reference = cellOf('al_casereference');
    if (!reference) {
      invalid.push({ rowNumber, caseReference: null, reason: 'Missing IO reference (BR-001).', raw });
      continue;
    }
    if (seen.has(reference.toLowerCase())) {
      invalid.push({
        rowNumber,
        caseReference: reference,
        reason: 'Duplicate IO reference within this file.',
        raw,
      });
      continue;
    }

    const record: Omit<Al_outcomecasesBase, 'al_outcomecaseid'> = {
      al_name: reference,
      al_casereference: reference,
      al_casestatus: CASE_STATUS_IMPORTED,
      statecode: 0,
    };

    let rowError: string | null = null;
    for (const col of COLUMNS) {
      if (col.field === 'al_casereference' || !columnIndex.has(col.field)) continue;
      const value = cellOf(col.field);
      if (!value) continue;
      if (col.kind === 'text') {
        (record as Record<string, unknown>)[col.field] = value;
      } else if (col.kind === 'date') {
        const parsed = parseDate(value);
        if (!parsed) {
          rowError = `"${col.header}" is not a valid date: "${value}".`;
          break;
        }
        (record as Record<string, unknown>)[col.field] = parsed;
      } else {
        const choice = findChoice(col.choices!, value);
        if (choice === null) {
          rowError = `"${col.header}" value "${value}" is not an accepted option.`;
          break;
        }
        (record as Record<string, unknown>)[col.field] = choice;
      }
    }

    if (rowError) {
      invalid.push({ rowNumber, caseReference: reference, reason: rowError, raw });
      continue;
    }

    seen.add(reference.toLowerCase());
    valid.push({ rowNumber, reference, record, raw });
  }

  return { valid, invalid, fatal: null };
}
