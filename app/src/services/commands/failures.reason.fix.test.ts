import { describe, expect, it } from 'vitest';
import { classify } from './failures';
import { DEFAULT_FAILURE_MESSAGES, messageForFailure } from '../errors';

/**
 * Audit 2026-11-17: every failure without a prefix was classified 'unavailable', which the
 * user reads as "check your connection" - for a missing parameter or a missing privilege,
 * neither of which a connection has anything to do with.
 */
describe('a failure without a prefix', () => {
  it('is a server problem, not a connection problem, when the request arrived', () => {
    const failure = classify({
      error: { code: '0x80040203', message: "The parameter 'ExpectedRowVersion' is required." },
    });
    expect(failure.kind).toBe('unexpected');
    expect(failure.message).toBe('');
    expect(messageForFailure(failure)).toBe(DEFAULT_FAILURE_MESSAGES.unexpected);
    expect(messageForFailure(failure)).not.toContain('ExpectedRowVersion');
  });

  it('reads a platform privilege fault as a permission problem', () => {
    const failure = classify({
      error: {
        code: '0x80040220',
        message:
          'Principal user (Id=3c6f441a-2fa1-f111-b8dd-e4fade069307, type=8, roleCount=2) is ' +
          'missing prvWriteal_listoption privilege (Id=1f0e3cf3-2ea1-f111-b8dd-e4fade069307) ' +
          'on OTC=10789 for entity al_listoption.',
      },
    });
    expect(failure.kind).toBe('unauthorized');
    expect(messageForFailure(failure)).toBe(DEFAULT_FAILURE_MESSAGES.unauthorized);
    expect(messageForFailure(failure)).not.toContain('prv');
  });

  it('recognises the SecLib form of the same fault', () => {
    expect(classify({ message: 'SecLib::CheckPrivilege failed' }).kind).toBe('unauthorized');
  });

  it('is still a connection problem when the request never left the browser', () => {
    const failure = classify(new TypeError('Failed to fetch'));
    expect(failure.kind).toBe('unavailable');
    expect(messageForFailure(failure)).toBe(DEFAULT_FAILURE_MESSAGES.unavailable);
  });
});
