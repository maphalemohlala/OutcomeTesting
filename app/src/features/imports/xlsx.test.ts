import { describe, expect, it } from 'vitest';
import { readWorkbook, rowsToCsv, serialToIso, workbookToCsv } from './xlsx';

/**
 * The workbooks here are built in the test rather than committed, so no client data from
 * the supplied extract lands in the repository (design D8). Entries are stored rather than
 * deflated, which the reader supports and which keeps the builder to a few lines.
 */
function zip(files: { name: string; content: string }[]): ArrayBuffer {
  const encoder = new TextEncoder();
  const parts: Uint8Array[] = [];
  const central: Uint8Array[] = [];
  let offset = 0;

  for (const file of files) {
    const name = encoder.encode(file.name);
    const data = encoder.encode(file.content);

    const local = new Uint8Array(30 + name.length);
    const localView = new DataView(local.buffer);
    localView.setUint32(0, 0x04034b50, true);
    localView.setUint16(4, 20, true);
    localView.setUint32(18, data.length, true);
    localView.setUint32(22, data.length, true);
    localView.setUint16(26, name.length, true);
    local.set(name, 30);

    const entry = new Uint8Array(46 + name.length);
    const entryView = new DataView(entry.buffer);
    entryView.setUint32(0, 0x02014b50, true);
    entryView.setUint32(20, data.length, true);
    entryView.setUint32(24, data.length, true);
    entryView.setUint16(28, name.length, true);
    entryView.setUint32(42, offset, true);
    entry.set(name, 46);

    parts.push(local, data);
    central.push(entry);
    offset += local.length + data.length;
  }

  const centralSize = central.reduce((total, part) => total + part.length, 0);
  const end = new Uint8Array(22);
  const endView = new DataView(end.buffer);
  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(8, files.length, true);
  endView.setUint16(10, files.length, true);
  endView.setUint32(12, centralSize, true);
  endView.setUint32(16, offset, true);

  const all = [...parts, ...central, end];
  const size = all.reduce((total, part) => total + part.length, 0);
  const buffer = new Uint8Array(size);
  let at = 0;
  for (const part of all) {
    buffer.set(part, at);
    at += part.length;
  }
  return buffer.buffer;
}

/** A workbook whose single sheet carries the given `<row>` XML. */
function workbook(sheetRows: string, options: { shared?: string[]; dateStyle?: boolean } = {}): ArrayBuffer {
  const shared = options.shared ?? [];
  return zip([
    {
      name: 'xl/workbook.xml',
      content: '<workbook><sheets><sheet name="Tasks (12)" sheetId="1" r:id="rId1"/></sheets></workbook>',
    },
    {
      name: 'xl/_rels/workbook.xml.rels',
      content:
        '<Relationships><Relationship Id="rId1" Target="worksheets/sheet1.xml"/></Relationships>',
    },
    {
      name: 'xl/sharedStrings.xml',
      content: `<sst count="${shared.length}">${shared.map((s) => `<si><t>${s}</t></si>`).join('')}</sst>`,
    },
    {
      name: 'xl/styles.xml',
      // Style 0 is General; style 1 is a date format when the test asks for one.
      content:
        '<styleSheet><numFmts><numFmt numFmtId="165" formatCode="dd/mm/yyyy hh:mm"/></numFmts>' +
        `<cellXfs count="2"><xf numFmtId="0"/><xf numFmtId="${options.dateStyle ? 165 : 0}"/></cellXfs></styleSheet>`,
    },
    { name: 'xl/worksheets/sheet1.xml', content: `<worksheet><sheetData>${sheetRows}</sheetData></worksheet>` },
  ]);
}

describe('serialToIso', () => {
  it('reads a whole serial as a plain date', () => {
    expect(serialToIso(46240)).toBe('2026-08-06');
  });

  it('reads a fractional serial as a date and time', () => {
    // The checklist completion stamps in the supplied workbook look like this.
    expect(serialToIso(46252.57916666667)).toBe('2026-08-18T13:54:00');
  });

  it('rounds to the nearest second rather than truncating', () => {
    expect(serialToIso(46254.617361111108)).toBe('2026-08-20T14:49:00');
  });
});

describe('readWorkbook', () => {
  it('resolves shared strings', async () => {
    const rows = await readWorkbook(
      workbook('<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>', {
        shared: ['TaskID', 'Client'],
      }),
    );

    expect(rows).toEqual([['TaskID', 'Client']]);
  });

  it('reads an inline string', async () => {
    const rows = await readWorkbook(
      workbook('<row r="1"><c r="A1" t="inlineStr"><is><t>Tax Check</t></is></c></row>'),
    );

    expect(rows).toEqual([['Tax Check']]);
  });

  it('joins the runs of a rich-text string', async () => {
    // Excel splits a string that carries mixed formatting into runs. Reading only the first
    // would truncate an item name and send it to the server as unrecognised.
    const rows = await readWorkbook(
      zip([
        { name: 'xl/workbook.xml', content: '<workbook><sheets/></workbook>' },
        {
          name: 'xl/sharedStrings.xml',
          content: '<sst><si><r><t>Trust </t></r><r><t>Documentation Check</t></r></si></sst>',
        },
        {
          name: 'xl/worksheets/sheet1.xml',
          content: '<worksheet><sheetData><row r="1"><c r="A1" t="s"><v>0</v></c></row></sheetData></worksheet>',
        },
      ]),
    );

    expect(rows).toEqual([['Trust Documentation Check']]);
  });

  it('leaves a plain number alone', async () => {
    const rows = await readWorkbook(workbook('<row r="1"><c r="A1"><v>253925362</v></c></row>'));

    expect(rows).toEqual([['253925362']]);
  });

  it('resolves a date-styled serial but not an unstyled one', async () => {
    const rows = await readWorkbook(
      workbook('<row r="1"><c r="A1" s="1"><v>46240</v></c><c r="B1" s="0"><v>46240</v></c></row>', {
        dateStyle: true,
      }),
    );

    expect(rows).toEqual([['2026-08-06', '46240']]);
  });

  it('fills the gap left by a missing cell', async () => {
    const rows = await readWorkbook(workbook('<row r="1"><c r="A1"><v>1</v></c><c r="C1"><v>3</v></c></row>'));

    expect(rows).toEqual([['1', '', '3']]);
  });

  it('keeps a blank row so later row numbers still match the spreadsheet', async () => {
    const rows = await readWorkbook(
      workbook('<row r="1"><c r="A1"><v>1</v></c></row><row r="3"><c r="A3"><v>3</v></c></row>'),
    );

    expect(rows).toEqual([['1'], [], ['3']]);
  });

  it('decodes escaped markup in a value', async () => {
    const rows = await readWorkbook(
      workbook('<row r="1"><c r="A1" t="s"><v>0</v></c></row>', { shared: ['Smith &amp; Jones &lt;Ltd&gt;'] }),
    );

    expect(rows).toEqual([['Smith & Jones <Ltd>']]);
  });

  it('handles a self-closing empty cell', async () => {
    const rows = await readWorkbook(workbook('<row r="1"><c r="A1"/><c r="B1"><v>2</v></c></row>'));

    expect(rows).toEqual([['', '2']]);
  });

  it('refuses a file that is not a workbook', async () => {
    await expect(readWorkbook(new TextEncoder().encode('not a zip').buffer)).rejects.toThrow(
      /not a valid \.xlsx/,
    );
  });
});

describe('rowsToCsv', () => {
  it('quotes a cell carrying a comma, a quote or a newline', () => {
    expect(rowsToCsv([['a,b', 'c"d', 'e\nf', 'plain']])).toBe('"a,b","c""d","e\nf",plain\r\n');
  });

  it('transcribes a workbook to CSV under its own headers', async () => {
    const csv = await workbookToCsv(
      workbook(
        '<row r="1"><c r="A1" t="s"><v>0</v></c><c r="B1" t="s"><v>1</v></c></row>' +
          '<row r="2"><c r="A2"><v>253925362</v></c><c r="B2" s="1"><v>46240</v></c></row>',
        { shared: ['TaskID', 'DueDate'], dateStyle: true },
      ),
    );

    expect(csv).toBe('TaskID,DueDate\r\n253925362,2026-08-06\r\n');
  });
});
