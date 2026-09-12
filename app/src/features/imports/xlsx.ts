/**
 * A minimal `.xlsx` reader, written here rather than taken as a dependency.
 *
 * The Code App's only job with a workbook is transcription (2026-09-12 design §3): unzip
 * it, read the cells, resolve Excel's date serials to ISO, and hand the result on as CSV
 * under the extract's own headers. It maps nothing and validates nothing -- `ImportRules`
 * in the plug-in owns every rule, so the AD-003 boundary stays where it was.
 *
 * That job needs a zip reader and an XML scanner and no more, which is a fraction of any
 * spreadsheet library. Nothing here touches the DOM, so it runs unchanged in the browser
 * and under vitest's node environment.
 *
 * Not supported, because the extract does not use them: encrypted workbooks, the
 * `deflate64` method, and the 1900 leap-year bug's first sixty serials (a date before
 * 1900-03-01 would come back a day late; the extract carries 2026 dates).
 */

/** Built-in number formats that mean a date or a time. */
const BUILTIN_DATE_FORMATS = new Set([14, 15, 16, 17, 18, 19, 20, 21, 22, 45, 46, 47]);

const SIGNATURE_EOCD = 0x06054b50;
const SIGNATURE_CENTRAL = 0x02014b50;

interface ZipEntry {
  method: number;
  compressedSize: number;
  localOffset: number;
}

function findEndOfCentralDirectory(view: DataView): number {
  // The record is 22 bytes plus a comment of at most 64KB, so it lies in the last 64KB+22.
  const earliest = Math.max(0, view.byteLength - 22 - 0xffff);
  for (let i = view.byteLength - 22; i >= earliest; i -= 1) {
    if (view.getUint32(i, true) === SIGNATURE_EOCD) return i;
  }
  return -1;
}

/** Maps every entry in the archive by name, from the central directory. */
function readCentralDirectory(buffer: ArrayBuffer): Map<string, ZipEntry> {
  const view = new DataView(buffer);
  const bytes = new Uint8Array(buffer);
  const eocd = findEndOfCentralDirectory(view);
  if (eocd < 0) throw new Error('The file is not a valid .xlsx workbook.');

  const count = view.getUint16(eocd + 10, true);
  let offset = view.getUint32(eocd + 16, true);
  const entries = new Map<string, ZipEntry>();

  for (let i = 0; i < count; i += 1) {
    if (offset + 46 > buffer.byteLength || view.getUint32(offset, true) !== SIGNATURE_CENTRAL) break;
    const nameLength = view.getUint16(offset + 28, true);
    const extraLength = view.getUint16(offset + 30, true);
    const commentLength = view.getUint16(offset + 32, true);
    const name = new TextDecoder('utf-8').decode(bytes.subarray(offset + 46, offset + 46 + nameLength));
    entries.set(name, {
      method: view.getUint16(offset + 10, true),
      compressedSize: view.getUint32(offset + 20, true),
      localOffset: view.getUint32(offset + 42, true),
    });
    offset += 46 + nameLength + extraLength + commentLength;
  }

  return entries;
}

async function inflateRaw(bytes: Uint8Array<ArrayBuffer>): Promise<Uint8Array> {
  // BufferSource, not Uint8Array: that is what DecompressionStream's writable side accepts.
  const source = new ReadableStream<BufferSource>({
    start(controller) {
      controller.enqueue(bytes);
      controller.close();
    },
  });
  const inflated = source.pipeThrough(new DecompressionStream('deflate-raw'));
  return new Uint8Array(await new Response(inflated).arrayBuffer());
}

/** Reads one archive member as text, or null when the workbook does not carry it. */
async function readEntry(buffer: ArrayBuffer, entries: Map<string, ZipEntry>, name: string): Promise<string | null> {
  const entry = entries.get(name);
  if (!entry) return null;

  const view = new DataView(buffer);
  const bytes = new Uint8Array(buffer);
  const nameLength = view.getUint16(entry.localOffset + 26, true);
  const extraLength = view.getUint16(entry.localOffset + 28, true);
  const start = entry.localOffset + 30 + nameLength + extraLength;
  const raw = bytes.subarray(start, start + entry.compressedSize);

  if (entry.method === 0) return new TextDecoder('utf-8').decode(raw);
  if (entry.method === 8) return new TextDecoder('utf-8').decode(await inflateRaw(raw));
  throw new Error('The workbook uses a compression method this reader does not support.');
}

// ---- a very small XML scanner -------------------------------------------------------

interface XmlElement {
  attributes: string;
  inner: string;
}

/**
 * Every `<tag>` in the document, with its attribute text and its content. Adequate because
 * none of the elements read here nest inside another of the same name.
 */
function elements(xml: string, tag: string): XmlElement[] {
  const pattern = new RegExp(`<${tag}(\\s[^>]*?)?(?:/>|>([\\s\\S]*?)</${tag}>)`, 'g');
  const found: XmlElement[] = [];
  let match = pattern.exec(xml);
  while (match !== null) {
    found.push({ attributes: match[1] ?? '', inner: match[2] ?? '' });
    match = pattern.exec(xml);
  }
  return found;
}

function attribute(attributes: string, name: string): string | null {
  const match = new RegExp(`\\s${name}="([^"]*)"`).exec(attributes);
  return match ? decodeXml(match[1]) : null;
}

function decodeXml(value: string): string {
  return value
    .replace(/&#x([0-9a-fA-F]+);/g, (_, hex: string) => String.fromCodePoint(parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec: string) => String.fromCodePoint(parseInt(dec, 10)))
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&amp;/g, '&');
}

/** Concatenates every `<t>` in a block, which is how a rich-text run is stored. */
function textOf(xml: string): string {
  return elements(xml, 't')
    .map((t) => decodeXml(t.inner))
    .join('');
}

// ---- dates ---------------------------------------------------------------------------

/**
 * True when a format code describes a date or a time. Quoted literals and colour/condition
 * blocks are stripped first, so a currency format naming a month in text is not mistaken
 * for one.
 */
function isDateFormatCode(code: string): boolean {
  const bare = code.replace(/\[[^\]]*\]/g, '').replace(/"[^"]*"/g, '');
  return /[ymdhs]/i.test(bare);
}

/**
 * An Excel serial as ISO. A whole serial is a plain date; a fractional one carries a time,
 * which is what the checklist completion stamps are. Seconds are rounded, so
 * 46252.57916666667 reads as 13:54:00 rather than 13:53:59.
 */
export function serialToIso(serial: number): string {
  const seconds = Math.round(serial * 86400);
  const date = new Date(Date.UTC(1899, 11, 30) + seconds * 1000);
  const iso = date.toISOString();
  return seconds % 86400 === 0 ? iso.slice(0, 10) : iso.slice(0, 19);
}

function columnIndex(reference: string): number {
  let index = 0;
  for (const character of reference) {
    const code = character.charCodeAt(0);
    if (code < 65 || code > 90) break;
    index = index * 26 + (code - 64);
  }
  return index - 1;
}

// ---- the workbook --------------------------------------------------------------------

/** Number-format id per cell-style index, so a numeric cell can be told from a date. */
function readStyles(xml: string | null): number[] {
  if (!xml) return [];
  const custom = new Map<number, string>();
  for (const format of elements(xml, 'numFmt')) {
    const id = Number(attribute(format.attributes, 'numFmtId'));
    const code = attribute(format.attributes, 'formatCode');
    if (Number.isFinite(id) && code !== null) custom.set(id, code);
  }

  // Only the cellXfs block describes cells; cellStyleXfs describes named styles.
  const cellXfs = /<cellXfs[\s\S]*?<\/cellXfs>/.exec(xml);
  if (!cellXfs) return [];

  return elements(cellXfs[0], 'xf').map((xf) => {
    const id = Number(attribute(xf.attributes, 'numFmtId') ?? '0');
    if (!Number.isFinite(id)) return 0;
    if (BUILTIN_DATE_FORMATS.has(id)) return id;
    const code = custom.get(id);
    return code !== undefined && isDateFormatCode(code) ? id : 0;
  });
}

function isDateStyle(styles: number[], styleIndex: string | null): boolean {
  if (styleIndex === null) return false;
  const id = styles[Number(styleIndex)];
  return id !== undefined && id !== 0;
}

/** The first sheet's part name, resolved through the workbook relationships. */
function firstSheetPath(workbook: string | null, rels: string | null): string {
  if (workbook && rels) {
    const sheet = elements(workbook, 'sheet')[0];
    const id = sheet ? attribute(sheet.attributes, 'r:id') : null;
    if (id) {
      for (const relationship of elements(rels, 'Relationship')) {
        if (attribute(relationship.attributes, 'Id') === id) {
          const target = attribute(relationship.attributes, 'Target');
          if (target) return target.startsWith('/') ? target.slice(1) : `xl/${target.replace(/^\.\//, '')}`;
        }
      }
    }
  }
  return 'xl/worksheets/sheet1.xml';
}

/**
 * Reads the workbook's first sheet into rows of text.
 *
 * Rows are placed by their own row number, so a blank line in the spreadsheet stays a
 * blank line here and every rejection the server reports points at the row the user sees.
 */
export async function readWorkbook(buffer: ArrayBuffer): Promise<string[][]> {
  const entries = readCentralDirectory(buffer);

  const [sharedXml, stylesXml, workbookXml, relsXml] = await Promise.all([
    readEntry(buffer, entries, 'xl/sharedStrings.xml'),
    readEntry(buffer, entries, 'xl/styles.xml'),
    readEntry(buffer, entries, 'xl/workbook.xml'),
    readEntry(buffer, entries, 'xl/_rels/workbook.xml.rels'),
  ]);

  const shared = sharedXml ? elements(sharedXml, 'si').map((si) => textOf(si.inner)) : [];
  const styles = readStyles(stylesXml);

  const sheetXml = await readEntry(buffer, entries, firstSheetPath(workbookXml, relsXml));
  if (sheetXml === null) throw new Error('The workbook has no readable sheet.');

  const rows: string[][] = [];
  let expectedRow = 0;

  for (const row of elements(sheetXml, 'row')) {
    const declared = Number(attribute(row.attributes, 'r') ?? '0');
    const index = Number.isFinite(declared) && declared > 0 ? declared - 1 : expectedRow;
    while (rows.length < index) rows.push([]);
    expectedRow = index + 1;

    const cells: string[] = [];
    let nextColumn = 0;

    for (const cell of elements(row.inner, 'c')) {
      const reference = attribute(cell.attributes, 'r');
      const column = reference ? columnIndex(reference) : nextColumn;
      nextColumn = column + 1;
      while (cells.length < column) cells.push('');

      const type = attribute(cell.attributes, 't');
      const value = elements(cell.inner, 'v')[0];
      let text = '';

      if (type === 's') {
        const stringIndex = Number(value ? decodeXml(value.inner) : '');
        text = shared[stringIndex] ?? '';
      } else if (type === 'inlineStr') {
        text = textOf(cell.inner);
      } else if (type === 'b') {
        text = value && decodeXml(value.inner) === '1' ? 'TRUE' : 'FALSE';
      } else if (value) {
        text = decodeXml(value.inner);
        const numeric = Number(text);
        if (
          type !== 'str' &&
          type !== 'e' &&
          Number.isFinite(numeric) &&
          isDateStyle(styles, attribute(cell.attributes, 's'))
        ) {
          text = serialToIso(numeric);
        }
      }

      cells.push(text);
    }

    rows.push(cells);
  }

  return rows;
}

/** Quotes a cell that carries a delimiter, quote or newline. */
function csvCell(value: string): string {
  return /[",\r\n]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value;
}

export function rowsToCsv(rows: string[][]): string {
  return `${rows.map((row) => row.map(csvCell).join(',')).join('\r\n')}\r\n`;
}

/** Reads a workbook straight to the CSV the `al_ImportCases` command expects. */
export async function workbookToCsv(buffer: ArrayBuffer): Promise<string> {
  return rowsToCsv(await readWorkbook(buffer));
}
