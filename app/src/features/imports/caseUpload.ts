/**
 * The preview half of the Intelligent Office task extract import (2026-09-12 design).
 *
 * This is deliberately the same parser as `ImportRules` in the plug-in assembly, kept in
 * step with it by hand. It is an affordance, not a boundary: `al_ImportCases` re-parses and
 * re-validates every row server-side (AD-003), so a rule that disagrees between the two
 * shows a row as importable here and then rejects it there. When one changes, change both.
 */

/** al_casestatus for a freshly imported case (Imported, BR-001). */
const CASE_STATUS_IMPORTED = 120910580;

/** al_taxcheckrequired, the input DeriveRoute reads to pick the route (BR-004). */
export const TAX_CHECK_REQUIRED_YES = 120910560;
export const TAX_CHECK_REQUIRED_NO = 120910561;

/** al_preorpostcheck; every task in a Pre-Advice extract is a pre-check. */
const PRE_OR_POST_CHECK_PRE = 120910540;

/** Checklist slots the extract carries, whether or not they are used. */
const CHECKLIST_SLOTS = 10;

type ColumnKind = 'text' | 'date' | 'choice';

interface ColumnDef {
  /** Header text exactly as the extract carries it. */
  header: string;
  /** Target al_outcomecase column. */
  field: string;
  kind: ColumnKind;
  /** Numeric option-set map (value -> label) for choice columns. */
  choices?: Record<number, string>;
}

/**
 * Columns of the extract, under IO's own header names. The workbook is transcribed to CSV
 * without renaming anything, so this table and `ImportRules.Columns` describe the same file.
 * TaskID is the import key and the only mandatory column.
 */
const COLUMNS: ColumnDef[] = [
  { header: 'TaskID', field: 'al_casereference', kind: 'text' },
  { header: 'ServiceCaseSequentialRef', field: 'al_servicecaseref', kind: 'text' },
  { header: 'ClientRef', field: 'al_clientref', kind: 'text' },
  { header: 'Client', field: 'al_clientname', kind: 'text' },
  { header: 'AdviserName', field: 'al_advisername', kind: 'text' },
  { header: 'AdviserEmail', field: 'al_adviseremail', kind: 'text' },
  // AD-113: the name the file carried, not proof of allocation.
  { header: 'AssignedTo', field: 'al_checkername', kind: 'text' },
  // The paraplanner who raised the pre-advice check task (project owner, 2026-09-12).
  { header: 'AssignedBy', field: 'al_paraplanner', kind: 'text' },
  {
    header: 'Status',
    field: 'al_iotaskstatus',
    kind: 'choice',
    choices: { 120910620: 'Not Started', 120910621: 'In Progress', 120910622: 'Complete' },
  },
  {
    header: 'Outcome',
    field: 'al_iooutcome',
    kind: 'choice',
    choices: {
      120910610: 'Pass',
      120910611: 'Pass with issues',
      120910612: 'Insufficient evidence',
      120910613: 'Potential harm',
    },
  },
  { header: 'CompletedBy', field: 'al_iocompletedby', kind: 'text' },
  { header: 'CompletedDate', field: 'al_iocompleteddate', kind: 'date' },
  { header: 'StartDate', field: 'al_taskstartdate', kind: 'date' },
  { header: 'DueDate', field: 'al_duedate', kind: 'date' },
  { header: 'CreatedDate', field: 'al_iocreateddate', kind: 'date' },
  { header: 'CreatedBy', field: 'al_iocreatedby', kind: 'text' },
  { header: 'TaskType', field: 'al_tasktype', kind: 'text' },
  { header: 'WorkflowName', field: 'al_workflowname', kind: 'text' },
  { header: 'ServiceStatus', field: 'al_servicestatus', kind: 'text' },
];

/**
 * The reasons a paraplanner can select inside the IO task, and whether each calls for a Tax
 * check (client, 2026-09-11). The two Tax items carry the short names the client used as
 * well as the wording the workbook exports.
 */
const CHECKLIST_ITEMS: Record<string, boolean> = {
  'tax check': true,
  tax: true,
  'trust documentation check': true,
  'trust documentation': true,
  'high risk item 1': false,
  'high risk item 2': false,
  'enhanced supervision': false,
  'pre-cas adviser': false,
  leaver: false,
};

/** The canonical name for each accepted alias. */
const CHECKLIST_CANONICAL_NAMES: Record<string, string> = {
  tax: 'Tax Check',
  'trust documentation': 'Trust Documentation Check',
};

export interface ParsedCase {
  rowNumber: number;
  reference: string;
  record: Record<string, unknown>;
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

/** One row per rejected/skipped case, so a user can correct and re-upload (FR-002). */
export interface ValidationReportRow {
  rowNumber: number;
  caseReference: string | null;
  status: string;
  reason: string;
  raw: string;
}

export function buildValidationReportCsv(rows: ValidationReportRow[]): string {
  const header = ['Row', 'TaskID', 'Status', 'Reason', 'Original row'].map(csvCell).join(',');
  const body = rows
    .map((r) =>
      [String(r.rowNumber), r.caseReference ?? '', r.status, r.reason, r.raw].map(csvCell).join(','),
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
 * Accepts yyyy-mm-dd (what the workbook reader emits), dd/mm/yyyy, and written-out forms
 * like "31 Jan 2026"; returns yyyy-mm-dd. Kept in step with `ImportRules.ParseDate`.
 *
 * A numeric date matching neither accepted order is rejected rather than handed to
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
  // midnight, and `toISOString()` on that lands on the 30th anywhere east of UTC.
  return isoIfReal(parsed.getFullYear(), parsed.getMonth() + 1, parsed.getDate());
}

/**
 * A date that may carry a time, as the checklist completion stamps do. Returns the value
 * unchanged when it already reads as ISO, so the stamp keeps its time.
 */
function parseDateTime(value: string): string | null {
  const trimmed = value.trim();
  const iso = /^(\d{4})-(\d{2})-(\d{2})[T ](\d{2}):(\d{2})(?::(\d{2}))?$/.exec(trimmed);
  if (iso) {
    const day = isoIfReal(Number(iso[1]), Number(iso[2]), Number(iso[3]));
    return day === null ? null : `${day}T${iso[4]}:${iso[5]}:${iso[6] ?? '00'}`;
  }
  return parseDate(trimmed);
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

export interface ChecklistSelection {
  /** Selected item names, canonicalised, in the order the row carried them. */
  items: string[];
  completedBy: string | null;
  completedOn: string | null;
  /** True when a selected item calls for a Tax check. */
  requiresTax: boolean;
  /** Set when the row cannot be routed; the row is rejected (BR-002). */
  error: string | null;
}

/**
 * Reads the ChecklistItem/CompletedBy/CompletionDate triplets into the one selection the
 * case carries.
 *
 * An item counts as selected when its name is one we recognise **and** it carries a
 * completion stamp. The column number is never consulted, which makes this correct whether
 * IO packs a partial selection into the first free slots or lists every item in fixed slots
 * and stamps only the chosen ones (design §5).
 */
export function readChecklist(
  fields: string[],
  headerIndex: Map<string, number>,
): ChecklistSelection {
  const selection: ChecklistSelection = {
    items: [],
    completedBy: null,
    completedOn: null,
    requiresTax: false,
    error: null,
  };

  const cell = (header: string): string => {
    const index = headerIndex.get(header.toLowerCase());
    return index === undefined ? '' : (fields[index] ?? '').trim();
  };

  for (let slot = 1; slot <= CHECKLIST_SLOTS; slot += 1) {
    const name = cell(`ChecklistItem${slot}`);
    const by = cell(`CompletedBy${slot}`);
    const on = cell(`CompletionDate${slot}`);

    // No stamp means the paraplanner did not pick this item, whatever its name. Checked
    // before the name is recognised, so an item IO lists but nobody selected cannot fail
    // the row.
    if (name === '' || (by === '' && on === '')) continue;

    const key = name.toLowerCase();
    if (!Object.prototype.hasOwnProperty.call(CHECKLIST_ITEMS, key)) {
      selection.error = `Checklist item "${name}" is not recognised, so the review route cannot be determined.`;
      return selection;
    }

    selection.items.push(CHECKLIST_CANONICAL_NAMES[key] ?? name);
    if (CHECKLIST_ITEMS[key]) selection.requiresTax = true;

    if (by !== '' && selection.completedBy === null) selection.completedBy = by;

    const stamp = parseDateTime(on);
    if (stamp !== null && (selection.completedOn === null || stamp < selection.completedOn)) {
      selection.completedOn = stamp;
    }
  }

  if (selection.items.length === 0) {
    selection.error = 'No checklist items are selected, so the review route cannot be determined.';
  }

  return selection;
}

/**
 * Parses an Intelligent Office task extract into cases to create and rows to flag.
 * Only TaskID is mandatory (BR-001); other columns are validated only when present.
 * No business rule is invented — an unrecognised choice or an unreadable date is an
 * exception carrying its reason, never a silent default.
 */
export function parseCaseCsv(text: string): ParseResult {
  const rows = tokenise(text).filter((r) => r.some((c) => c.trim() !== ''));
  if (rows.length === 0) {
    return { valid: [], invalid: [], fatal: 'The file is empty.' };
  }

  // Indexed by the extract's own header names, because the checklist triplets are addressed
  // by name too and are not columns in the table above.
  const headerIndex = new Map<string, number>();
  rows[0].forEach((header, index) => {
    const key = header.trim().toLowerCase();
    if (key !== '' && !headerIndex.has(key)) headerIndex.set(key, index);
  });

  if (!headerIndex.has('taskid')) {
    return {
      valid: [],
      invalid: [],
      fatal: 'The file is missing the "TaskID" column. Use the Intelligent Office task extract.',
    };
  }

  const valid: ParsedCase[] = [];
  const invalid: RowError[] = [];
  const seen = new Set<string>();

  for (let r = 1; r < rows.length; r += 1) {
    const fields = rows[r];
    const rowNumber = r + 1;
    const raw = fields.map(csvCell).join(',').slice(0, 2000);
    const cellOf = (header: string): string => {
      const index = headerIndex.get(header.toLowerCase());
      return index === undefined ? '' : (fields[index] ?? '').trim();
    };

    const reference = cellOf('TaskID');
    if (!reference) {
      invalid.push({ rowNumber, caseReference: null, reason: 'Missing TaskID (BR-001).', raw });
      continue;
    }
    // One task is one case, so two tasks on one service case both import. It is a repeated
    // TaskID that is the error.
    if (seen.has(reference.toLowerCase())) {
      invalid.push({
        rowNumber,
        caseReference: reference,
        reason: 'Duplicate TaskID within this file.',
        raw,
      });
      continue;
    }

    const record: Record<string, unknown> = {
      al_name: reference,
      al_casereference: reference,
      al_casestatus: CASE_STATUS_IMPORTED,
      statecode: 0,
    };

    let rowError: string | null = null;
    for (const col of COLUMNS) {
      if (col.field === 'al_casereference' || !headerIndex.has(col.header.toLowerCase())) continue;
      const value = cellOf(col.header);
      if (!value) continue;
      if (col.kind === 'text') {
        record[col.field] = value;
      } else if (col.kind === 'date') {
        const parsed = parseDate(value);
        if (!parsed) {
          rowError = `"${col.header}" is not a valid date: "${value}".`;
          break;
        }
        record[col.field] = parsed;
      } else {
        const choice = findChoice(col.choices!, value);
        if (choice === null) {
          rowError = `"${col.header}" value "${value}" is not an accepted option.`;
          break;
        }
        record[col.field] = choice;
      }
    }

    // Every task in a Pre-Advice extract is a pre-check; the extract has no column for it.
    if (cellOf('TaskType').toLowerCase().startsWith('pre-advice')) {
      record.al_preorpostcheck = PRE_OR_POST_CHECK_PRE;
    }

    // The checklist is the route's only input now, so a row whose checklist cannot be read
    // is rejected rather than created without one.
    if (rowError === null) {
      const checklist = readChecklist(fields, headerIndex);
      if (checklist.error !== null) {
        rowError = checklist.error;
      } else {
        record.al_checklistitems = checklist.items.join('\n');
        record.al_taxcheckrequired = checklist.requiresTax
          ? TAX_CHECK_REQUIRED_YES
          : TAX_CHECK_REQUIRED_NO;
        if (checklist.completedBy !== null) record.al_checklistcompletedby = checklist.completedBy;
        if (checklist.completedOn !== null) record.al_checklistcompleteddate = checklist.completedOn;
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
