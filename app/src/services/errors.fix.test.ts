import { describe, expect, it } from 'vitest';
import { classify } from './commands/failures';
import { DEFAULT_FAILURE_MESSAGES, messageForFailure } from './errors';

describe('an unexpected server fault', () => {
  it('shows a plain sentence, not the plug-in or table names', () => {
    const failure = classify({
      message:
        "UNEXPECTED: OutcomeTesting.Plugins.AssignCasePlugin could not complete. OrganizationServiceFault: Entity 'al_reviewinstance' With Id = 1f0e Does Not Exist",
    });
    const shown = messageForFailure(failure);
    expect(shown).not.toContain('al_reviewinstance');
    expect(shown).not.toContain('OutcomeTesting.Plugins');
    expect(shown).toBe(DEFAULT_FAILURE_MESSAGES.unexpected);
  });

  it('still shows a rule speaking deliberately', () => {
    const failure = classify({ message: 'VALIDATION: Choose a checker.' });
    expect(messageForFailure(failure)).toBe('Choose a checker.');
  });

  // Owner decision 2026-11-17: the server now says what failed and why, in plain words.
  it('shows the server sentence as-is when it is in the friendly form', () => {
    const sentence =
      'Allocating the case could not be completed because it took too long. Nothing was saved. Please try again.';
    const failure = classify({
      error: { code: '0x80040265', message: `UNEXPECTED: ${sentence}` },
    });
    expect(failure.kind).toBe('unexpected');
    expect(messageForFailure(failure)).toBe(sentence);
  });

  it.each([
    'Saving the case could not be completed because OutcomeTesting.Plugins.X failed.',
    'Saving the case could not be completed because NullReferenceException: Object reference not set.',
    'Saving the case could not be completed because OrganizationServiceFault: denied.',
    'Saving the case could not be completed because Entity With Id = 12 Does Not Exist.',
    'Saving the case could not be completed because row 1f0e3cf3-2ea1-f111-b8dd-e4fade069307 is gone.',
    'Saving the case could not be completed because al_outcomecase is locked.',
  ])('falls back to the generic sentence when the text still looks technical: %s', (sentence) => {
    const failure = classify({ message: `UNEXPECTED: ${sentence}` });
    expect(messageForFailure(failure)).toBe(DEFAULT_FAILURE_MESSAGES.unexpected);
  });

  it('falls back to the generic sentence for an unexpected fault not in the friendly form', () => {
    const failure = classify({ message: 'UNEXPECTED: Something broke.' });
    expect(messageForFailure(failure)).toBe(DEFAULT_FAILURE_MESSAGES.unexpected);
  });
});

describe('a server that cannot be reached', () => {
  it('says what to check, and claims nothing about what was saved', () => {
    const failure = classify(new TypeError('Failed to fetch'));
    expect(failure.kind).toBe('unavailable');
    const shown = messageForFailure(failure);
    expect(shown).toBe(
      'The app could not reach the server. Check your connection, reload the app and sign in again if asked, then check whether the change was made before trying again.',
    );
    // It can follow a step that did save (case details saved, then the allocation lost).
    expect(shown).not.toMatch(/nothing was (saved|changed)/i);
  });
});
