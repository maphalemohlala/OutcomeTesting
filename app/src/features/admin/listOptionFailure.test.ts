import { describe, expect, it } from 'vitest';
import {
  DELETE_FALLBACK,
  OPTION_IN_USE,
  SAVE_FALLBACK,
  UNREACHABLE,
  describeDeleteFailure,
  describeSaveFailure,
} from './listOptionFailure';

/**
 * The refusal an administrator is shown when an option cannot be deleted.
 *
 * The fixture is the real one: DEV refused a delete of the option case 900000005 holds, and
 * this is what came back. Pinning the actual payload rather than a paraphrase is the point -
 * a matcher written against a message somebody imagined is a matcher that has never met the
 * error it exists for.
 */
const RESTRICT_REFUSAL = {
  error: {
    code: '0x80040227',
    message:
      'The object you tried to delete is associated with another object and cannot be ' +
      'deleted. Deleting records from al_ListOption (ObjectTypeCode: 10789)\r\nfor Id(s) : ' +
      'e70229be-cdb5-f111-aaac-e4fade069307\r\nException : Cascade Delete failed due to ' +
      'cascade restrict relation. Details below : \r\nRestricting entity al_OutcomeCase ' +
      '(ObjectTypeCode: 10635) has Id: 21c85650-f8b4-f111-aaac-e4fade069307 and is ' +
      'collected by Relationship with name: al_listoption_al_outcomecase_producttype.\r\n',
  },
};

describe('deleting an option a case is using', () => {
  it('says cases are using it, and says what to do instead', () => {
    expect(describeDeleteFailure(RESTRICT_REFUSAL)).toBe(OPTION_IN_USE);
  });

  it('recognises it by the error code alone, whatever the wording becomes', () => {
    // The sentence is Dataverse's and can change under us; the code is the stable part.
    expect(describeDeleteFailure({ error: { code: '0x80040227', message: 'Nope.' } })).toBe(
      OPTION_IN_USE,
    );
  });

  it('recognises it from a thrown Error as well as a returned payload', () => {
    expect(describeDeleteFailure(new Error(RESTRICT_REFUSAL.error.message))).toBe(OPTION_IN_USE);
  });

  it('is not reported as a network problem when the browser thinks it is offline', () => {
    // A Restrict refusal is a real answer from Dataverse. Calling it a connection failure
    // would send the administrator to check their wifi over a decision the server made.
    expect(describeDeleteFailure(RESTRICT_REFUSAL, false)).toBe(OPTION_IN_USE);
  });

  it('never leaks the record ids or the relationship name into what a person reads', () => {
    // The refusal names a case id, an object type code and the relationship. None of that
    // belongs on screen, and the standing rule is that internals do not reach a user.
    const shown = describeDeleteFailure(RESTRICT_REFUSAL);
    expect(shown).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-/);
    expect(shown).not.toContain('al_listoption');
    expect(shown).not.toContain('ObjectTypeCode');
  });
});

describe('everything else', () => {
  it('reports an unreachable connection as one, and says nothing was changed', () => {
    expect(describeDeleteFailure(new TypeError('Failed to fetch'))).toBe(UNREACHABLE);
    expect(describeSaveFailure(new TypeError('Failed to fetch'))).toBe(UNREACHABLE);
    expect(describeSaveFailure('anything', false)).toBe(UNREACHABLE);
  });

  it('falls back to a plain sentence for a fault it does not recognise', () => {
    expect(describeDeleteFailure({ error: { code: '0x8004xxxx' } })).toBe(DELETE_FALLBACK);
    expect(describeSaveFailure({ error: { code: '0x8004xxxx' } })).toBe(SAVE_FALLBACK);
  });

  it('survives an error it cannot read at all', () => {
    const circular: Record<string, unknown> = { message: 'went wrong' };
    circular.self = circular;
    expect(describeDeleteFailure(circular)).toBe(DELETE_FALLBACK);
    expect(describeDeleteFailure(null)).toBe(DELETE_FALLBACK);
    expect(describeDeleteFailure(undefined)).toBe(DELETE_FALLBACK);
  });
});
