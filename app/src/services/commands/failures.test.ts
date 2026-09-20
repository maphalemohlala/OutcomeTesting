import { describe, expect, it } from 'vitest';
import { classify, extractErrorMessage, plainMessage, sentenceAfterPrefix } from './failures';
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

/**
 * F12, found in DEV on 2026-09-20. Saving a notification letter whose body sanitises away
 * put THIS on the administrator's screen, in the dialog, verbatim:
 *
 *   {"error":{"code":"0x80040265","message":"Nothing in that body is ...",
 *    "@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.ExceptionFromPluginExecute":
 *      "OutcomeTesting.Plugins.NotificationTemplateGuardPlugin",
 *    "@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.PluginTrace":"... Initiating User:
 *      3c6f441a-2fa1-f111-b8dd-e4fade069307 ...", ...}}
 *
 * The plug-in's own sentence is good and is sitting inside it. What reached the screen was
 * the plug-in class name, the table name, the step registration id, the plug-in trace with
 * timings, and the initiating user's GUID - the same NFR-OBS-01 failure as F1, on the
 * client this time.
 *
 * This guard raises a bare sentence with no VALIDATION: prefix, so the prefix branch never
 * matched it and the editor fell back to `error.message`, which IS the whole body.
 */
describe('plainMessage unwraps an OData body', () => {
  const guardBody = JSON.stringify({
    error: {
      code: '0x80040265',
      message:
        'Nothing in that body is formatting this system keeps, so saving it would leave the letter empty.',
      '@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.ExceptionFromPluginExecute':
        'OutcomeTesting.Plugins.NotificationTemplateGuardPlugin',
      '@Microsoft.PowerApps.CDS.ErrorDetails.Plugin.PluginTrace':
        '[+405ms] - [Execute] - Entered ... Initiating User: 3c6f441a-2fa1-f111-b8dd-e4fade069307',
      '@Microsoft.PowerApps.CDS.InnerError.Message':
        'Nothing in that body is formatting this system keeps, so saving it would leave the letter empty.',
    },
  });

  it('returns the sentence the plug-in wrote', () => {
    expect(plainMessage({ message: guardBody })).toBe(
      'Nothing in that body is formatting this system keeps, so saving it would leave the letter empty.',
    );
  });

  it('names no plug-in, table, trace or user', () => {
    const shown = plainMessage({ message: guardBody });

    expect(shown).not.toContain('NotificationTemplateGuardPlugin');
    expect(shown).not.toContain('PluginTrace');
    expect(shown).not.toContain('Initiating User');
    expect(shown).not.toContain('@Microsoft.PowerApps.CDS');
    expect(shown).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/);
  });

  it('unwraps a body handed over as the error itself, not as a message', () => {
    expect(plainMessage(JSON.parse(guardBody))).toContain('Nothing in that body');
  });

  it('leaves a plain sentence alone', () => {
    expect(plainMessage({ message: 'That wording could not be saved.' })).toBe(
      'That wording could not be saved.',
    );
  });

  it('still strips a prefix a rule did write', () => {
    expect(plainMessage({ message: 'VALIDATION: Pick a recipient first.' })).toBe(
      'Pick a recipient first.',
    );
  });

  it('gives nothing back rather than a body it cannot read', () => {
    expect(plainMessage({ message: '{"unparseable' })).toBe('');
  });
});
