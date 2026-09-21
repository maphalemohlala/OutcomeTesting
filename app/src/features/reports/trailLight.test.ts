import { describe, expect, it } from 'vitest';
import {
  TRAIL_LIGHT_HEADERS,
  paraplannerEmailIsGenerated,
  trailLightRow,
  type ExportRecord,
} from './trailLight';

function record(overrides: Partial<ExportRecord> = {}): ExportRecord {
  return {
    al_exportrecordid: 'rec-1',
    al_exportrecordcode: 'EXP-1',
    al_name: 'EXP-1',
    ...overrides,
  } as ExportRecord;
}

describe('Trail Light contract (AD-039)', () => {
  it('holds the AD-039 twenty in their fixed positions', () => {
    // The receiving system reads by position, so these five are the contract. Changing what
    // a column MEANS is allowed by direction; moving one is not.
    expect(TRAIL_LIGHT_HEADERS[0]).toBe('Adviser name');
    expect(TRAIL_LIGHT_HEADERS[9]).toBe('File Quality Grade');
    expect(TRAIL_LIGHT_HEADERS[14]).toBe('Advice Quality Grade');
    expect(TRAIL_LIGHT_HEADERS[15]).toBe('');
    expect(TRAIL_LIGHT_HEADERS[19]).toBe('Advice Quality Fail Accountable Paraplanner Code');
  });

  it('carries emails in columns B and D, and no code columns there', () => {
    // Project owner, 2026-09-21: "replace column B & D on the trail light exports with
    // emails. rather than codes show emails".
    expect(TRAIL_LIGHT_HEADERS[1]).toBe('Adviser Email');
    expect(TRAIL_LIGHT_HEADERS[3]).toBe('Paraplanner Email');
  });

  it('is back to twenty columns, the appended adviser email having gone', () => {
    // Column 21 held Adviser Email from 2026-09-19. With column B carrying it, keeping 21
    // would only repeat the value; the project owner chose to drop it on 2026-09-21.
    // Removing the LAST column moves nothing before it.
    expect(TRAIL_LIGHT_HEADERS).toHaveLength(20);
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Adviser Code');
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Paraplanner Code');
  });

  it('writes the two emails into columns B and D', () => {
    const row = trailLightRow(
      record({
        al_adviseremail: 'jane.adviser@example.com',
        al_paraplanneremail: 'sam.paraplanner@example.com',
      }),
    );

    expect(row).toHaveLength(20);
    expect(row[1]).toBe('jane.adviser@example.com');
    expect(row[3]).toBe('sam.paraplanner@example.com');
  });

  it('leaves B or D blank rather than falling back to the code or the name', () => {
    // AD-039 reads by position, so column D is "Paraplanner Email" on every row or the file
    // lies about the rows where it is something else. GenerateExportPlugin refuses to guess
    // between two contacts of one name, so an empty cell is a real outcome here.
    const row = trailLightRow(record({ al_advisercode: 'ADV-01', al_paraplannercode: 'PP-01' }));

    expect(row[1]).toBe('');
    expect(row[3]).toBe('');
  });

  it('writes one value per header, in the header order', () => {
    const row = trailLightRow(
      record({
        al_advisername: 'Jane Adviser',
        al_adviseremail: 'jane.adviser@example.com',
        al_paraplannername: 'Sam Paraplanner',
        al_paraplanneremail: 'sam.paraplanner@example.com',
        al_casetype: 'New advice',
        al_productsolutiontype: 'Accumulation Pension',
        al_checkdate: '2026-02-05T00:00:00Z',
        al_clientname: 'A. Client',
        al_preorpostcheck: 'Pre',
        al_filequalitygrade: 'Fail',
        al_fqfailadvisername: 'Jane Adviser',
        al_fqfailadvisercode: '1234',
        al_advicequalitygrade: 'Potential harm',
        al_aqfailparaplannername: 'Sam Paraplanner',
        al_aqfailparaplannercode: '5678',
      }),
    );

    expect(row).toHaveLength(TRAIL_LIGHT_HEADERS.length);
    expect(row[0]).toBe('Jane Adviser');
    expect(row[6]).toBe('2026-02-05');
    expect(row[9]).toBe('Fail');
    expect(row[14]).toBe('Potential harm');
    expect(row[15]).toBe('');
    expect(row[19]).toBe(5678);
  });

  it('still types the four fail-accountable codes as numbers', () => {
    // Only B and D were asked for. These four name a person picked out of a fail rather
    // than the case's own adviser and para-planner, and they stay codes.
    const row = trailLightRow(
      record({ al_fqfailadvisercode: '42', al_aqfailparaplannercode: 'ADV-01' }),
    );

    expect(row[11]).toBe(42);
    expect(row[19]).toBe('ADV-01');
  });

  it('leaves the accountable pair blank where nobody is accountable', () => {
    const row = trailLightRow(record());

    expect(row[11]).toBe('');
    expect(row[12]).toBe('');
  });

  it('still hand-declares the paraplanner email column', () => {
    // Created in DEV on 2026-09-21, ahead of the generated model. When a regeneration adds
    // it, GeneratedHasParaplannerEmail becomes true, the assignment in trailLight.ts stops
    // compiling, and this test is the reminder of what to delete: the intersection on
    // ExportRecord, the guard, and this case.
    expect(paraplannerEmailIsGenerated).toBe(false);
  });
});
