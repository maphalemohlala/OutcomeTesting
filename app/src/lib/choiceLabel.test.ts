import { describe, expect, it } from 'vitest';
import { choiceLabel } from './choiceLabel';

/** A stand-in for a generated choice map, in the same 1209107xx block the solution uses. */
const STATUS = {
  120910770: 'Draft',
  120910771: 'Generated',
  120910772: 'Exported',
} as const;

describe('choiceLabel', () => {
  it('prefers the formatted value the platform supplied', () => {
    expect(choiceLabel(STATUS, 120910770, 'Draft')).toBe('Draft');
  });

  it('falls back to the map when the read omitted the formatted value', () => {
    // This is the case that matters: getAll returns the option number and no *name, which
    // is how a raw "120910771" reached the exports screen.
    expect(choiceLabel(STATUS, 120910771, undefined)).toBe('Generated');
  });

  it('treats a blank formatted value as absent rather than as a label', () => {
    expect(choiceLabel(STATUS, 120910772, '   ')).toBe('Exported');
  });

  it('returns null for a value no map recognises, never the number', () => {
    // Returning the number would put a Dataverse internal in front of a user with no way to
    // translate it. Null lets the caller show an honest placeholder instead.
    expect(choiceLabel(STATUS, 120910799, undefined)).toBeNull();
  });

  it('returns null when there is no value at all', () => {
    expect(choiceLabel(STATUS, undefined, undefined)).toBeNull();
  });

  it('does not mistake option value zero for absent', () => {
    // statecode 0 is Active. A falsy check here would report every active row as unset.
    expect(choiceLabel({ 0: 'Active', 1: 'Inactive' }, 0, undefined)).toBe('Active');
  });
});
