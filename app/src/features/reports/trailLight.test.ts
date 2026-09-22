import { describe, expect, it } from 'vitest';
import { TRAIL_LIGHT_HEADERS, trailLightRow, type ExportRecord } from './trailLight';

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
