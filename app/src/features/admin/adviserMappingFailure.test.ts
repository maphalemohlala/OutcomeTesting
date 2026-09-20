import { describe, expect, it } from 'vitest';
import {
  alreadyMappedRefusal,
  describeRemoveFailure,
  describeSaveFailure,
  DUPLICATE_ADVISER,
  REMOVE_FALLBACK,
  SAVE_FALLBACK,
  UNREACHABLE,
} from './adviserMappingFailure';
import type { AdviserMappingRow } from './adviserMappingRows';

/**
 * F17, found in DEV on 2026-09-20. Mapping an adviser who was already mapped and saving
 * with the network pulled both produced the same sentence: "That mapping could not be
 * saved." The behaviour underneath was right in both cases — the duplicate is refused by a
 * Dataverse alternate key on al_adviseremail, and the offline save wrote nothing — and
 * nothing from the fault leaked. The administrator was simply told nothing they could act
 * on, on a page whose other messages are unusually good.
 */

// The body Dataverse actually returned on 2026-09-20, trimmed.
const duplicateBody = JSON.stringify({
  error: {
    code: '0x80060892',
    '@Microsoft.PowerApps.CDS.ErrorDetails.DuplicateAttributes':
      '<DuplicateAttributes><al_adviseremail>adviser@example.invalid</al_adviseremail></DuplicateAttributes>',
    '@Microsoft.PowerApps.CDS.InnerError.Message':
      'Entity Key Adviser email violated. A record with the same value for Adviser email already exists. A duplicate record cannot be created.',
  },
});

describe('describeSaveFailure', () => {
  it('names the duplicate and says what to do instead', () => {
    const shown = describeSaveFailure(duplicateBody, true);

    expect(shown).toBe(DUPLICATE_ADVISER);
    expect(shown).toContain('already mapped');
    expect(shown).toContain('Change');
  });

  it('recognises the duplicate from the error code alone', () => {
    expect(describeSaveFailure({ code: '0x80060892' }, true)).toBe(DUPLICATE_ADVISER);
  });

  it('says the connection failed when the browser is offline', () => {
    expect(describeSaveFailure(new TypeError('Failed to fetch'), false)).toBe(UNREACHABLE);
    expect(describeSaveFailure(UNREACHABLE, false)).toBe(UNREACHABLE);
  });

  it('recognises a fetch failure even when the browser thinks it is online', () => {
    expect(describeSaveFailure(new TypeError('Failed to fetch'), true)).toBe(UNREACHABLE);
    expect(describeSaveFailure({ message: 'net::ERR_INTERNET_DISCONNECTED' }, true)).toBe(
      UNREACHABLE,
    );
  });

  it('prefers the duplicate over the connection when both could be read', () => {
    // A duplicate is a real answer from the server, so the request plainly arrived.
    expect(describeSaveFailure(duplicateBody, false)).toBe(DUPLICATE_ADVISER);
  });

  it('falls back to the plain sentence for anything it cannot name', () => {
    expect(describeSaveFailure({ code: '0x80040265' }, true)).toBe(SAVE_FALLBACK);
    expect(describeSaveFailure(null, true)).toBe(SAVE_FALLBACK);
  });

  it('leaks nothing from the fault', () => {
    for (const online of [true, false]) {
      const shown = describeSaveFailure(duplicateBody, online);
      expect(shown).not.toContain('DuplicateAttributes');
      expect(shown).not.toContain('0x80060892');
      expect(shown).not.toContain('al_adviseremail');
      expect(shown).not.toContain('Microsoft.PowerApps');
    }
  });

  it('survives an error that cannot be serialised', () => {
    const circular: Record<string, unknown> = { message: 'Failed to fetch' };
    circular.self = circular;

    expect(describeSaveFailure(circular, true)).toBe(UNREACHABLE);
  });
});

describe('describeRemoveFailure', () => {
  it('says the connection failed when it did', () => {
    expect(describeRemoveFailure(new TypeError('Failed to fetch'), false)).toBe(UNREACHABLE);
  });

  it('otherwise says the removal did not happen', () => {
    expect(describeRemoveFailure({ code: '0x80040265' }, true)).toBe(REMOVE_FALLBACK);
    expect(describeRemoveFailure({ code: '0x80040265' }, true)).not.toContain('saved');
  });
});

describe('alreadyMappedRefusal', () => {
  const mappings: AdviserMappingRow[] = [
    {
      id: 'm1',
      adviserEmail: 'Adviser@Example.invalid',
      managerId: 'c1',
      managerName: 'A Manager',
    },
  ];

  it('refuses an adviser the page can already see mapped', () => {
    expect(alreadyMappedRefusal('adviser@example.invalid', mappings)).toBe(DUPLICATE_ADVISER);
  });

  it('ignores case and surrounding space, as the alternate key does', () => {
    expect(alreadyMappedRefusal('  ADVISER@EXAMPLE.INVALID  ', mappings)).toBe(DUPLICATE_ADVISER);
  });

  it('allows an adviser who is not mapped', () => {
    expect(alreadyMappedRefusal('someone@example.invalid', mappings)).toBeNull();
  });

  it('says nothing about an empty choice, which the form refuses for its own reason', () => {
    expect(alreadyMappedRefusal('', mappings)).toBeNull();
    expect(alreadyMappedRefusal('   ', mappings)).toBeNull();
  });
});
