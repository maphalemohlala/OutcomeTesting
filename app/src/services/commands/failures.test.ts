import { describe, expect, it } from 'vitest';
import { classify, extractErrorMessage, sentenceAfterPrefix } from './failures';
import { DEFAULT_FAILURE_MESSAGES } from '../errors';

/**
 * The body below is the real fault the Exports page surfaced when Generate was pressed
 * on a batch that had already been generated: the plug-in's sentence followed by the
 * whole CDS diagnostic block. Every character of it reached the screen, because
 * classify sliced from the prefix to the end of the string.
 */
const GENERATED_TWICE_FAULT = {
  message:
    '{"error":{"code":"0x80040265","message":"PRECONDITION: This export batch has ' +
    'already been generated. Create a new batch to produce a fresh export.",' +
    '"@Microsoft.PowerApps.CDS.ErrorDetails.OperationStatus":"0",' +
    '"@Microsoft.PowerApps.CDS.ErrorDetails.SubErrorCode":"-2146233088",' +
    '"@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.ExceptionFromPluginExecute":' +
    '"OutcomeTesting.Plugins.GenerateExportPlugin"}}',
};

describe('extractErrorMessage', () => {
  it('reaches a message nested behind an error wrapper', () => {
    expect(extractErrorMessage({ error: { message: 'inner reason' } })).toBe('inner reason');
  });
});

describe('sentenceAfterPrefix', () => {
  it('ends the sentence at the JSON string boundary', () => {
    expect(sentenceAfterPrefix('x":"NOTFOUND: No such case.","code":"1"', 'NOTFOUND:')).toBe(
      'No such case.',
    );
  });

  it('returns the whole remainder when there is no boundary', () => {
    expect(sentenceAfterPrefix('CONFLICT: Someone else saved first.', 'CONFLICT:')).toBe(
      'Someone else saved first.',
    );
  });
});

describe('classify', () => {
  it('keeps only the sentence after the prefix, not the CDS diagnostics', () => {
    const failure = classify(GENERATED_TWICE_FAULT);

    expect(failure.ok).toBe(false);
    expect(failure.kind).toBe('precondition');
    expect(failure.message).toBe(
      'This export batch has already been generated. Create a new batch to produce a fresh export.',
    );
  });

  it('does not leak diagnostic keys or JSON punctuation', () => {
    const failure = classify(GENERATED_TWICE_FAULT);

    expect(failure.message).not.toContain('@Microsoft');
    expect(failure.message).not.toContain('"');
    expect(failure.message).not.toContain('\\');
  });

  it('handles a body that was stringified, so the quotes arrive escaped', () => {
    const failure = classify(
      '{\\"message\\":\\"VALIDATION: Case reference is required.\\",\\"code\\":\\"1\\"}',
    );

    expect(failure.kind).toBe('validation');
    expect(failure.message).toBe('Case reference is required.');
  });

  it('classifies an unprefixed failure as unavailable without leaking the cause', () => {
    const failure = classify({ message: 'socket hang up at 10.0.0.1' });

    expect(failure.kind).toBe('unavailable');
    expect(failure.message).toBe(DEFAULT_FAILURE_MESSAGES.unavailable);
    expect(failure.message).not.toContain('10.0.0.1');
  });
});

/**
 * The 2026-09-10 report: a portal submit refused, and every layer above the plug-in had
 * only "something went wrong" to say, because the exception was not one PluginBase caught
 * and so left the pipeline unclassifiable. PluginBase now wraps it; this is what the app
 * makes of that wrapper.
 */
describe('an unexpected plug-in failure', () => {
  const WRAPPED = {
    error: {
      code: '0x80040265',
      message:
        'UNEXPECTED: OutcomeTesting.Plugins.SubmitRequestPlugin could not complete. ' +
        'NullReferenceException: Object reference not set to an instance of an object.',
    },
  };

  it('is classified rather than falling through to the system-level default', () => {
    const failure = classify(WRAPPED);
    expect(failure.ok).toBe(false);
    if (failure.ok) return;
    expect(failure.kind).toBe('unexpected');
    expect(failure.message).not.toBe(DEFAULT_FAILURE_MESSAGES.unavailable);
  });

  it('names the plug-in and the error, so the message says what happened', () => {
    const failure = classify(WRAPPED);
    if (failure.ok) return;
    expect(failure.message).toContain('SubmitRequestPlugin');
    expect(failure.message).toContain('NullReferenceException');
  });

  it('keeps the stack out of it - only what PluginBase chose to say', () => {
    const failure = classify(WRAPPED);
    if (failure.ok) return;
    expect(failure.message).not.toContain('at OutcomeTesting');
  });
});
