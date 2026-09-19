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
  // Read off detail.header, the single eighteen-field list the Checker Checklist draws two
  // to a row (project owner, 2026-09-13). It replaced four themed panels - Client, Adviser
  // and paraplanner, Advice and product, Check and tax - which the document has no trace of,
  // so the labels here are the document's own ("For Tax team usage", not "Tax team
  // disposition") and are shared with the review page's header through caseHeaderFields.
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

    expect(valueOf(detail.header, 'Tax check required')).toBe('Yes');
    expect(valueOf(detail.header, 'For Tax team usage')).toBe('Submit to AQS');
    expect(valueOf(detail.header, 'Vulnerable client?')).toBe('Potentially vulnerable');
    expect(valueOf(detail.header, 'Case type')).toBe('Ongoing');
    expect(valueOf(detail.header, 'Product / solution type')).toBe('IHT');
    expect(valueOf(detail.header, 'Sample source')).toBe('Mandatory');
    expect(valueOf(detail.header, 'Pre or post check')).toBe('Pre');
  });

  it('prefers the formatted name when Dataverse does supply it', () => {
    const detail = toDetail(
      record({ al_taxcheckrequired: 120910560, al_taxcheckrequiredname: 'Yes (formatted)' }),
    );
    expect(valueOf(detail.header, 'Tax check required')).toBe('Yes (formatted)');
  });

  it('reports an unset choice as absent rather than guessing', () => {
    const detail = toDetail(record({}));
    expect(valueOf(detail.header, 'Tax check required')).toBeNull();
  });

  it('keeps the edit value and the displayed label in agreement', () => {
    const detail = toDetail(record({ al_taxcheckrequired: 120910560 }));
    expect(detail.edit.al_taxcheckrequired).toBe(120910560);
    expect(valueOf(detail.header, 'Tax check required')).toBe('Yes');
  });
});


describe('toDetail checklist items', () => {
  // The IO task's items, which the case page draws through the same ChecklistSection the
  // review page uses (project owner, 2026-09-19). The column is a newline-separated list
  // written by the import, so blank lines and stray whitespace are the import's own
  // formatting rather than items.
  it('splits the column into the items the paraplanner ticked', () => {
    const detail = toDetail(
      record({ al_checklistitems: 'High Risk Item 1\nTax Check' } as Partial<Al_outcomecases>),
    );

    expect(detail.checklist.items).toEqual(['High Risk Item 1', 'Tax Check']);
  });

  it('names no items when the case carries none', () => {
    // A case imported before the extract carried the columns. The section draws nothing.
    expect(toDetail(record({})).checklist.items).toEqual([]);
  });

  it('ignores blank lines and surrounding whitespace', () => {
    const detail = toDetail(
      record({ al_checklistitems: ' Tax Check \n\n' } as Partial<Al_outcomecases>),
    );

    expect(detail.checklist.items).toEqual(['Tax Check']);
  });
});
