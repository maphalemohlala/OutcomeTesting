/*
 * Minimal .xlsx writer. Produces a valid Office Open XML workbook with one sheet per
 * table, using inline strings so no shared-string part is needed and stored (uncompressed)
 * zip entries so no deflate implementation is needed.
 *
 * Written here rather than pulled from npm because the Code App bundle ships to the Power
 * Apps player: this is the whole of what the export screens need, and it keeps the
 * dependency surface of a regulated application unchanged.
 */

export type CellValue = string | number | null | undefined;

export interface Sheet {
  name: string;
  headers: string[];
  rows: CellValue[][];
}

const encoder = new TextEncoder();

export function escapeXml(value: string): string {
  return (
    value
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;')
      .replace(/'/g, '&apos;')
      // Control characters are not representable in XML 1.0, so Excel rejects the whole
      // workbook if one survives. Matching them is the point, hence the rule suppression.
      // eslint-disable-next-line no-control-regex
      .replace(/[\u0000-\u0008\u000B\u000C\u000E-\u001F]/g, '')
  );
}

/** 0 -> A, 25 -> Z, 26 -> AA. */
export function columnRef(index: number): string {
  let ref = '';
  let n = index;
  while (n >= 0) {
    ref = String.fromCharCode(65 + (n % 26)) + ref;
    n = Math.floor(n / 26) - 1;
  }
  return ref;
}

/** Excel rejects these characters in a sheet name, and truncates at 31. */
export function safeSheetName(name: string, fallback: string): string {
  const cleaned = name.replace(/[[\]:*?/\\]/g, ' ').trim();
  return (cleaned || fallback).slice(0, 31);
}

function cellXml(value: CellValue, ref: string): string {
  if (value === null || value === undefined || value === '') return '';
  if (typeof value === 'number' && Number.isFinite(value)) {
    return `<c r="${ref}"><v>${value}</v></c>`;
  }
  return `<c r="${ref}" t="inlineStr"><is><t xml:space="preserve">${escapeXml(String(value))}</t></is></c>`;
}

function rowXml(values: CellValue[], rowNumber: number): string {
  const cells = values.map((value, index) => cellXml(value, `${columnRef(index)}${rowNumber}`)).join('');
  return `<row r="${rowNumber}">${cells}</row>`;
}

export function sheetXml(sheet: Sheet): string {
  const header = rowXml(sheet.headers, 1);
  const body = sheet.rows.map((row, index) => rowXml(row, index + 2)).join('');
  return (
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">' +
    `<sheetData>${header}${body}</sheetData>` +
    '</worksheet>'
  );
}

const CRC_TABLE = (() => {
  const table = new Uint32Array(256);
  for (let i = 0; i < 256; i += 1) {
    let c = i;
    for (let k = 0; k < 8; k += 1) {
      c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    }
    table[i] = c >>> 0;
  }
  return table;
})();

export function crc32(bytes: Uint8Array): number {
  let crc = 0xffffffff;
  for (let i = 0; i < bytes.length; i += 1) {
    crc = CRC_TABLE[(crc ^ bytes[i]) & 0xff] ^ (crc >>> 8);
  }
  return (crc ^ 0xffffffff) >>> 0;
}

interface ZipEntry {
  path: string;
  data: Uint8Array;
}

/** Stored-method zip. Sizes are well under the 4 GB point where zip64 becomes necessary. */
function zip(entries: ZipEntry[]): Uint8Array {
  const parts: Uint8Array[] = [];
  const directory: Uint8Array[] = [];
  let offset = 0;

  for (const entry of entries) {
    const name = encoder.encode(entry.path);
    const crc = crc32(entry.data);
    const size = entry.data.length;

    const local = new Uint8Array(30 + name.length);
    const localView = new DataView(local.buffer);
    localView.setUint32(0, 0x04034b50, true);
    localView.setUint16(4, 20, true);
    localView.setUint16(6, 0, true);
    localView.setUint16(8, 0, true);
    localView.setUint16(10, 0, true);
    localView.setUint16(12, 0x0021, true); // 1980-01-01: a fixed date, so output is reproducible
    localView.setUint32(14, crc, true);
    localView.setUint32(18, size, true);
    localView.setUint32(22, size, true);
    localView.setUint16(26, name.length, true);
    localView.setUint16(28, 0, true);
    local.set(name, 30);

    const central = new Uint8Array(46 + name.length);
    const centralView = new DataView(central.buffer);
    centralView.setUint32(0, 0x02014b50, true);
    centralView.setUint16(4, 20, true);
    centralView.setUint16(6, 20, true);
    centralView.setUint16(8, 0, true);
    centralView.setUint16(10, 0, true);
    centralView.setUint16(12, 0, true);
    centralView.setUint16(14, 0x0021, true);
    centralView.setUint32(16, crc, true);
    centralView.setUint32(20, size, true);
    centralView.setUint32(24, size, true);
    centralView.setUint16(28, name.length, true);
    centralView.setUint32(42, offset, true);
    central.set(name, 46);

    parts.push(local, entry.data);
    directory.push(central);
    offset += local.length + size;
  }

  const directorySize = directory.reduce((total, part) => total + part.length, 0);
  const end = new Uint8Array(22);
  const endView = new DataView(end.buffer);
  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(8, entries.length, true);
  endView.setUint16(10, entries.length, true);
  endView.setUint32(12, directorySize, true);
  endView.setUint32(16, offset, true);

  const total = offset + directorySize + end.length;
  const output = new Uint8Array(total);
  let cursor = 0;
  for (const part of [...parts, ...directory, end]) {
    output.set(part, cursor);
    cursor += part.length;
  }
  return output;
}

export function buildWorkbook(sheets: Sheet[]): Uint8Array {
  if (sheets.length === 0) throw new Error('A workbook needs at least one sheet.');

  const named = sheets.map((sheet, index) => ({
    ...sheet,
    name: safeSheetName(sheet.name, `Sheet${index + 1}`),
  }));

  const contentTypes =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">' +
    '<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>' +
    '<Default Extension="xml" ContentType="application/xml"/>' +
    '<Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>' +
    named
      .map(
        (_, index) =>
          `<Override PartName="/xl/worksheets/sheet${index + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>`,
      )
      .join('') +
    '</Types>';

  const rootRels =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
    '<Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>' +
    '</Relationships>';

  const workbook =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">' +
    '<sheets>' +
    named
      .map(
        (sheet, index) =>
          `<sheet name="${escapeXml(sheet.name)}" sheetId="${index + 1}" r:id="rId${index + 1}"/>`,
      )
      .join('') +
    '</sheets></workbook>';

  const workbookRels =
    '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' +
    '<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">' +
    named
      .map(
        (_, index) =>
          `<Relationship Id="rId${index + 1}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet${index + 1}.xml"/>`,
      )
      .join('') +
    '</Relationships>';

  return zip([
    { path: '[Content_Types].xml', data: encoder.encode(contentTypes) },
    { path: '_rels/.rels', data: encoder.encode(rootRels) },
    { path: 'xl/workbook.xml', data: encoder.encode(workbook) },
    { path: 'xl/_rels/workbook.xml.rels', data: encoder.encode(workbookRels) },
    ...named.map((sheet, index) => ({
      path: `xl/worksheets/sheet${index + 1}.xml`,
      data: encoder.encode(sheetXml(sheet)),
    })),
  ]);
}
