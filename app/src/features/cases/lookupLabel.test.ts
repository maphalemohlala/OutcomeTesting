import { describe, expect, it } from 'vitest';
import { lookupLabel } from './lookupLabel';

const ANNOTATION = '_al_reviewrouteid_value@OData.Community.Display.V1.FormattedValue';

describe('lookupLabel', () => {
  it('reads the OData formatted-value annotation Dataverse actually returns', () => {
    expect(lookupLabel({ [ANNOTATION]: 'Tax then AQS' }, 'al_reviewrouteid')).toBe('Tax then AQS');
  });

  it('falls back to the generated *name field when no annotation is present', () => {
    expect(lookupLabel({}, 'al_reviewrouteid', 'AQS only')).toBe('AQS only');
  });

  it('prefers the annotation over the *name field', () => {
    expect(lookupLabel({ [ANNOTATION]: 'Tax only' }, 'al_reviewrouteid', 'AQS only')).toBe(
      'Tax only',
    );
  });

  it('reports an empty lookup as absent rather than blank text', () => {
    expect(lookupLabel({ [ANNOTATION]: '   ' }, 'al_reviewrouteid', '  ')).toBeNull();
    expect(lookupLabel({}, 'al_reviewrouteid')).toBeNull();
  });
});
