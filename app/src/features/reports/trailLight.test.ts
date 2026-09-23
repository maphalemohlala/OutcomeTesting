import { describe, expect, it } from 'vitest';
import {
  TRAIL_LIGHT_HEADERS,
  trailLightFilename,
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

  it('carries codes in columns B and D', () => {
    // Project owner, 2026-09-22: Trailight could not accommodate the emails put here on
    // 2026-09-21 (AD-183), so the columns carry codes again. The codes now come from the
    // registry rather than from the case, which is what makes them non-empty.
    expect(TRAIL_LIGHT_HEADERS[1]).toBe('Adviser Code');
    expect(TRAIL_LIGHT_HEADERS[3]).toBe('Paraplanner Code');
  });

  it('is still twenty columns, the meaning of B and D having changed but not their place', () => {
    expect(TRAIL_LIGHT_HEADERS).toHaveLength(20);
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Adviser Email');
    expect(TRAIL_LIGHT_HEADERS).not.toContain('Paraplanner Email');
  });

  it('writes the two codes into columns B and D, numeric ones as numbers', () => {
    const row = trailLightRow(
      record({ al_advisercode: '4471', al_paraplannercode: 'PP-01' }),
    );

    expect(row).toHaveLength(20);
    expect(row[1]).toBe(4471);
    expect(row[3]).toBe('PP-01');
  });

  it('leaves B or D blank rather than falling back to a name or an address', () => {
    // AD-039 reads by position: column B is the adviser's code on every row, or the file
    // lies about the rows where it is something else. A person the registry does not hold
    // a code for is a real outcome here.
    const row = trailLightRow(
      record({ al_advisername: 'Jane Adviser', al_paraplannername: 'Sam Paraplanner' }),
    );

    expect(row[1]).toBe('');
    expect(row[3]).toBe('');
  });

  it('writes one value per header, in the header order', () => {
    const row = trailLightRow(
      record({
        al_advisername: 'Jane Adviser',
        al_advisercode: '4471',
        al_paraplannername: 'Sam Paraplanner',
        al_paraplannercode: 'PP-01',
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
});

describe('what a Trail Light download is called', () => {
  it('is DFALIN1_outcometesting_yyyy_mm_dd, exactly', () => {
    // The receiving end's name for this feed (project owner, 2026-09-23). A file that
    // arrives under another name is a file nobody picks up, so this is as much part of the
    // interface as the twenty columns are.
    expect(trailLightFilename('xlsx', new Date('2026-09-23T10:00:00Z'))).toBe(
      'DFALIN1_outcometesting_2026_09_23.xlsx',
    );
    expect(trailLightFilename('csv', new Date('2026-09-23T10:00:00Z'))).toBe(
      'DFALIN1_outcometesting_2026_09_23.csv',
    );
  });

  it('uses UNDERSCORES in the date, not the hyphens everything else stamps with', () => {
    // stampedFilename writes `stem-YYYY-MM-DD.ext`. This is a different convention because
    // it was given as a different convention, and it is not improved by being made
    // consistent with something it is not part of.
    const name = trailLightFilename('csv', new Date('2026-01-05T09:00:00Z'));

    expect(name).toBe('DFALIN1_outcometesting_2026_01_05.csv');
    expect(name).not.toContain('-');
  });

  it('pads a single-digit month and day, so the name sorts and parses', () => {
    expect(trailLightFilename('xlsx', new Date('2026-03-07T12:00:00Z'))).toBe(
      'DFALIN1_outcometesting_2026_03_07.xlsx',
    );
  });

  it('names the UK day, not the browser day', () => {
    /*
     * 2026-06-01T23:30Z is already the 2nd of June in London under British Summer Time.
     * A machine reading UTC would name a daily feed for yesterday, which is the failure
     * this shares with the case header's date handling - and the same ukToday fixes both.
     */
    expect(trailLightFilename('csv', new Date('2026-06-01T23:30:00Z'))).toBe(
      'DFALIN1_outcometesting_2026_06_02.csv',
    );
  });

  it('carries no batch code, so the day has ONE name', () => {
    // Deliberate: the convention describes the day's file, not the click that made it.
    const a = trailLightFilename('xlsx', new Date('2026-09-23T08:00:00Z'));
    const b = trailLightFilename('xlsx', new Date('2026-09-23T17:00:00Z'));

    expect(a).toBe(b);
  });
});
