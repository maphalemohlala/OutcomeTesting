import type { CellValue, Sheet } from '../../lib/tabular';

/**
 * Platform bookkeeping that carries no business meaning. Excluding it keeps a sheet
 * readable; everything else the caller is permitted to read is written as Dataverse
 * returned it, logical names included, because this is a raw analysis extract rather
 * than an operational screen.
 */
const NOISE = new Set([
  'importsequencenumber',
  'overriddencreatedon',
  'timezoneruleversionnumber',
  'utcconversiontimezonecode',
  'versionnumber',
  'owneridtype',
]);

/** The suffix Dataverse puts on a field's human-readable value. */
const FORMATTED = '@OData.Community.Display.V1.FormattedValue';

function isNoise(key: string): boolean {
  return NOISE.has(key) || key.endsWith('yominame');
}

/**
 * The column a key becomes, or null to leave it out.
 *
 * Logical names are deliberate here — an analysis extract is joined against Dataverse and
 * `al_duedate` is what it is joined on. OData's own annotations are not: `@odata.etag` and
 * `@Microsoft.Dynamics.CRM.lookuplogicalname` are transport plumbing, and they were
 * arriving as column headings, 33 of the 91 on the Cases sheet (F20, 2026-09-20).
 *
 * The formatted value is the exception worth keeping. It is the readable form of the field
 * beside it — "Active" for a statecode, a person's name for a lookup — so it is kept under
 * a heading that names the field rather than the annotation, and the raw value stays for
 * joining.
 */
export function headerFor(key: string): string | null {
  if (isNoise(key)) return null;
  if (key.endsWith(FORMATTED)) {
    const base = key.slice(0, -FORMATTED.length);
    return base === '' ? null : `${base} (label)`;
  }
  // Everything else carrying an @ is transport, including @odata.etag, the lookup logical
  // name and the navigation-property annotations.
  if (key.includes('@')) return null;
  return key;
}

function normalise(value: unknown): CellValue {
  if (value === null || value === undefined) return '';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'number') return Number.isFinite(value) ? value : '';
  if (typeof value === 'string') return value;
  return '';
}

/** A readable tab for a table that is readable but holds nothing. */
export const EMPTY_SHEET_NOTE = 'This table has no rows.';

/**
 * One sheet from the rows of one table.
 *
 * A table with no rows still gets a header and a line saying so. It used to get neither, so
 * the workbook carried a completely blank tab — Sign-offs, in the first extract taken — which
 * reads as a broken file rather than as an empty table.
 */
export function toSheet(name: string, records: Record<string, unknown>[]): Sheet {
  const headers: string[] = [];
  const sources: string[] = [];

  for (const record of records) {
    for (const [key, value] of Object.entries(record)) {
      if (value !== null && typeof value === 'object') continue;
      const header = headerFor(key);
      if (header === null || headers.includes(header)) continue;
      headers.push(header);
      sources.push(key);
    }
  }

  if (headers.length === 0) {
    return { name, headers: ['Note'], rows: [[EMPTY_SHEET_NOTE]] };
  }

  return {
    name,
    headers,
    rows: records.map((record) => sources.map((source) => normalise(record[source]))),
  };
}
