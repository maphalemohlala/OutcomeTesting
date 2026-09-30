import { describe, expect, it } from 'vitest';
import { REINSTATE_FIELDS, optionUpdateFields, type OptionDraft } from './listOptions';

/**
 * What an edit to an option actually sends.
 *
 * The Power Apps client serialises an update with JSON.stringify, which DROPS a key whose value
 * is undefined. So `{ al_effectiveto: undefined }` reaches Dataverse as `{}`: the PATCH succeeds,
 * nothing changes, and the page reloads showing the option still retired - which is exactly
 * how "Reinstate" failed. Only null tells Dataverse to clear a column. These assertions are on
 * the serialised body because that is where the value was being lost.
 */
const wire = (fields: object) => JSON.parse(JSON.stringify(fields)) as Record<string, unknown>;

const draft = (over: Partial<OptionDraft> = {}): OptionDraft => ({
  id: 'opt-1',
  label: '  IHT  ',
  sortOrder: '3',
  effectiveFrom: '2026-09-01',
  effectiveTo: '2026-09-20',
  ...over,
});

describe('reinstating a retired option', () => {
  it('sends the retired date as null, so Dataverse clears it', () => {
    expect(wire(REINSTATE_FIELDS)).toEqual({ al_effectiveto: null });
  });
});

describe('saving a change to an option', () => {
  it('sends every field it holds', () => {
    expect(wire(optionUpdateFields(draft()))).toEqual({
      al_name: 'IHT',
      al_sortorder: 3,
      al_effectivefrom: '2026-09-01',
      al_effectiveto: '2026-09-20',
    });
  });

  it('clears a retired date the administrator blanked, rather than leaving it', () => {
    expect(wire(optionUpdateFields(draft({ effectiveTo: null })))).toHaveProperty('al_effectiveto', null);
  });

  it('clears a start date the administrator blanked', () => {
    expect(wire(optionUpdateFields(draft({ effectiveFrom: null })))).toHaveProperty('al_effectivefrom', null);
  });

  it('clears a blanked sort order instead of writing 0 or leaving the old one', () => {
    expect(wire(optionUpdateFields(draft({ sortOrder: '  ' })))).toHaveProperty('al_sortorder', null);
  });
});
