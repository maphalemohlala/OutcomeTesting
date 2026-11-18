import { describe, expect, it } from 'vitest';
import * as advisers from './adviserMappingFailure';
import * as lists from './listOptionFailure';
import { FAIL_POINT_SAVE_FALLBACK, describeFailPointSaveFailure } from './failReasons';
import { DEFAULT_FAILURE_MESSAGES } from '../../services/errors';

/**
 * Audit 2026-11-17: these pages replaced every server sentence with a fallback that says
 * what failed but not why. A refusal the server worded itself is the why, so it is shown.
 */
const RETIRED = {
  error: {
    code: '0x80040265',
    message:
      'VALIDATION: This option has been retired, so it cannot be chosen for a case.",' +
      '"@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.ExceptionFromPluginExecute":"X"',
  },
};
const RETIRED_SENTENCE = 'This option has been retired, so it cannot be chosen for a case.';

const FRIENDLY_UNEXPECTED =
  'Saving the option could not be completed because it took too long. Nothing was saved. Please try again.';
const TECHNICAL_UNEXPECTED =
  'UNEXPECTED: OutcomeTesting.Plugins.X could not complete. NullReferenceException: boom.';

const PRIVILEGE = {
  error: { code: '0x80040220', message: 'Principal user is missing prvWriteal_x privilege.' },
};

const describers: [string, (e: unknown, online?: boolean) => string, string][] = [
  ['adviser mapping save', advisers.describeSaveFailure, advisers.SAVE_FALLBACK],
  ['adviser mapping remove', advisers.describeRemoveFailure, advisers.REMOVE_FALLBACK],
  ['list option save', lists.describeSaveFailure, lists.SAVE_FALLBACK],
  ['list option delete', lists.describeDeleteFailure, lists.DELETE_FALLBACK],
  ['fail point save', describeFailPointSaveFailure, FAIL_POINT_SAVE_FALLBACK],
];

describe.each(describers)('%s', (_name, describeFailure, fallback) => {
  it('shows the reason a refusal gave', () => {
    expect(describeFailure(RETIRED, true)).toBe(RETIRED_SENTENCE);
  });

  it('shows the reason even when the browser thinks it is offline, since the server answered', () => {
    expect(describeFailure(RETIRED, false)).toBe(RETIRED_SENTENCE);
  });

  it('shows an unexpected fault the server worded plainly', () => {
    expect(describeFailure({ message: `UNEXPECTED: ${FRIENDLY_UNEXPECTED}` }, true)).toBe(
      FRIENDLY_UNEXPECTED,
    );
  });

  it('keeps its own fallback for an unexpected fault that still looks technical', () => {
    expect(describeFailure({ message: TECHNICAL_UNEXPECTED }, true)).toBe(fallback);
  });

  it('says a missing privilege is a permission problem', () => {
    expect(describeFailure(PRIVILEGE, true)).toBe(DEFAULT_FAILURE_MESSAGES.unauthorized);
  });

  it('keeps its own fallback when there is no reason to give', () => {
    expect(describeFailure({ error: { code: '0x8004xxxx' } }, true)).toBe(fallback);
    expect(describeFailure({ message: 'VALIDATION: ' }, true)).toBe(fallback);
  });
});
