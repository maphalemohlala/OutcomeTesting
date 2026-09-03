import { describe, expect, it } from 'vitest';
import { TRAIL_LIGHT_HEADERS, trailLightRow } from './trailLight';
import type { Al_exportrecords } from '../../generated/models/Al_exportrecordsModel';

function record(overrides: Partial<Al_exportrecords> = {}): Al_exportrecords {
  return {
    al_exportrecordid: 'rec-1',
    al_exportrecordcode: 'EXP-1',
    al_name: 'EXP-1',
    ...overrides,
  } as Al_exportrecords;
}

describe('Trail Light contract (AD-039)', () => {
  it('is exactly twenty columns with the blank separator at position 16', () => {
    expect(TRAIL_LIGHT_HEADERS).toHaveLength(20);
    expect(TRAIL_LIGHT_HEADERS[15]).toBe('');
    expect(TRAIL_LIGHT_HEADERS[0]).toBe('Adviser name');
    expect(TRAIL_LIGHT_HEADERS[14]).toBe('Advice Quality Grade');
    expect(TRAIL_LIGHT_HEADERS[19]).toBe('Advice Quality Fail Accountable Paraplanner Code');
  });

  it('writes one value per header, in the header order', () => {
    const row = trailLightRow(
      record({
        al_advisername: 'Jane Adviser',
        al_advisercode: '1234',
        al_paraplannername: 'Sam Paraplanner',
        al_paraplannercode: '5678',
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

  it('types a numeric code as a number and leaves the pair blank where nobody is accountable', () => {
    const row = trailLightRow(record({ al_advisercode: '42' }));
    expect(row[1]).toBe(42);
    expect(row[11]).toBe('');
    expect(row[12]).toBe('');
  });

  it('passes a non-numeric code through instead of coercing it to zero', () => {
    expect(trailLightRow(record({ al_advisercode: 'ADV-01' }))[1]).toBe('ADV-01');
  });
});
