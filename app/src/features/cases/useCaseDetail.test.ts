import { describe, expect, it } from 'vitest';
import { toDetail } from './caseDetailMapping';
import type { Al_outcomecases } from '../../generated/models/Al_outcomecasesModel';

/**
 * Dataverse returns the numeric option value but not the `*name` formatted value for
 * choice columns on a single-record read, so labels must come from the generated maps.
 */
function record(overrides: Partial<Al_outcomecases>): Al_outcomecases {
  return {
    al_outcomecaseid: 'case-1',
    al_casereference: 'IO-100001',
    al_casestatus: 120910580,
    ...overrides,
  } as Al_outcomecases;
}

function valueOf(fields: { label: string; value: string | null }[], label: string) {
  return fields.find((field) => field.label === label)?.value;
}

describe('toDetail choice labels', () => {
  it('labels every choice from its numeric value when the formatted name is absent', () => {
    const detail = toDetail(
      record({
        al_taxcheckrequired: 120910560,
        al_vulnerableclient: 120910552,
        al_adviserstatus: 120910500,
        al_casetype: 120910511,
        al_productsolutiontype: 120910522,
        al_samplesource: 120910531,
        al_preorpostcheck: 120910540,
        al_taxteamdisposition: 120910570,
      }),
    );

    expect(valueOf(detail.checkAndTax, 'Tax check required')).toBe('Yes');
    expect(valueOf(detail.checkAndTax, 'Tax team disposition')).toBe('Submit to AQS');
    expect(valueOf(detail.client, 'Vulnerable client')).toBe('Potentially vulnerable');
    expect(valueOf(detail.adviceAndProduct, 'Case type')).toBe('Ongoing');
    expect(valueOf(detail.adviceAndProduct, 'Product/solution type')).toBe('IHT');
    expect(valueOf(detail.adviceAndProduct, 'Sample source')).toBe('Mandatory');
    expect(valueOf(detail.adviceAndProduct, 'Check point')).toBe('Pre');
  });

  it('prefers the formatted name when Dataverse does supply it', () => {
    const detail = toDetail(
      record({ al_taxcheckrequired: 120910560, al_taxcheckrequiredname: 'Yes (formatted)' }),
    );
    expect(valueOf(detail.checkAndTax, 'Tax check required')).toBe('Yes (formatted)');
  });

  it('reports an unset choice as absent rather than guessing', () => {
    const detail = toDetail(record({}));
    expect(valueOf(detail.checkAndTax, 'Tax check required')).toBeNull();
  });

  it('keeps the edit value and the displayed label in agreement', () => {
    const detail = toDetail(record({ al_taxcheckrequired: 120910560 }));
    expect(detail.edit.al_taxcheckrequired).toBe(120910560);
    expect(valueOf(detail.checkAndTax, 'Tax check required')).toBe('Yes');
  });
});
