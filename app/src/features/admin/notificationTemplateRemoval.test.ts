import { describe, expect, it } from 'vitest';
import {
  REMOVE_FALLBACK,
  UNREACHABLE,
  describeRemoveFailure,
  removalPrompt,
} from './notificationTemplateRemoval';
import type { TemplateRow } from './useNotificationTemplates';

/**
 * F24, found in DEV on 2026-09-20 while working APP-143.
 *
 * The Notification wording page could add a letter of your own and edit any letter, and
 * could undo neither. A custom letter attached to Allocation emailed somebody on every
 * allocation and there was no control anywhere that stopped it; a letter of the twelve whose
 * wording had been saved could never go back to the built-in copy — which is also why
 * APP-128 could not be run, since all twelve carried stored wording and no route existed to
 * clear one.
 *
 * The same shape as F18 on adviser mappings: a screen that creates state it cannot take
 * back. The server side was never the problem — every read of this table already filters to
 * the active row for a code, so removing the row is all that "off" has ever meant here.
 */

function row(overrides: Partial<TemplateRow> = {}): TemplateRow {
  return {
    id: 'a0000000-0000-0000-0000-000000000001',
    code: 'TELL-THE-MANAGER',
    name: 'Tell the manager',
    subject: 'Heads up on {{reference}}',
    body: 'Case {{reference}} needs a look.',
    tokens: ['reference'],
    isHtml: true,
    stored: true,
    custom: true,
    eventValue: 120910800,
    recipientKind: 120910813,
    recipientContactId: null,
    recipientContactName: null,
    ...overrides,
  };
}

describe('what removing a letter would do', () => {
  it('offers nothing for a letter that has no stored row', () => {
    // One of the twelve that nobody has edited is ALREADY on the built-in wording, so
    // offering to restore it would be offering to do nothing.
    expect(removalPrompt(row({ id: null, custom: false, stored: false }))).toBeNull();
  });

  it('names the letter, so the dialog is not about "this item"', () => {
    const prompt = removalPrompt(row())!;

    expect(prompt.lead).toContain('Tell the manager');
  });

  it('says a letter of your own stops being sent', () => {
    const prompt = removalPrompt(row())!;

    expect(prompt.title).toBe('Remove this letter');
    expect(prompt.consequence).toContain('stops being sent');
  });

  it('says what does NOT change when a letter of your own goes', () => {
    // APP-143's actual requirement: the built-in letter for that event still sends. An
    // administrator removing a letter from a page full of letters needs telling that they
    // are not switching the event off.
    const prompt = removalPrompt(row())!;

    expect(prompt.consequence).toMatch(/carries on unaltered/);
  });

  it('warns that the wording cannot be recovered', () => {
    // The row is deleted rather than deactivated, because the code is an alternate key an
    // inactive row keeps - the F18 trap. So this is the one warning that has to be true.
    expect(removalPrompt(row())!.lead).toContain('cannot be recovered');
    expect(removalPrompt(row({ custom: false }))!.lead).toContain('cannot be recovered');
  });

  it('frames one of the twelve as going back to the built-in wording, not as deletion', () => {
    // The letter keeps being sent. Calling this "remove" would read as switching off a
    // letter the system will carry on sending whatever this page does.
    const prompt = removalPrompt(row({ custom: false, code: 'ALLOCATION', name: 'Case allocated' }))!;

    expect(prompt.title).toBe('Use the built-in wording');
    expect(prompt.consequence).toContain('carries on being sent');
    expect(prompt.lead).not.toMatch(/stops/i);
  });

  it('says the recipient override goes back too', () => {
    // SettingsFor reads the recipient from the same row, so discarding the wording discards
    // the routing with it. An administrator who had pointed the letter somewhere would
    // otherwise find it quietly going back to the default with nothing having said so.
    expect(removalPrompt(row({ custom: false }))!.consequence).toMatch(/default/);
  });
});

describe('why a removal did not happen', () => {
  it('repeats a server refusal as the server wrote it', () => {
    // The shape NotificationTemplateGuardPlugin actually produces: a bare sentence inside an
    // OData body, observed against DEV over the Web API on 2026-09-20.
    const refusal = {
      message:
        '{"error":{"code":"0x80040265","message":"The Case allocated letter needs a body."}}',
    };

    expect(describeRemoveFailure(refusal)).toBe('The Case allocated letter needs a body.');
  });

  it('drops the prefix from a refusal that carries one', () => {
    // plainMessage strips it (F12), so the administrator reads a sentence rather than a
    // classification code they have no use for.
    const refusal = { message: '{"error":{"message":"VALIDATION: that letter is in use."}}' };

    expect(describeRemoveFailure(refusal)).toBe('that letter is in use.');
  });

  it('tells an unreachable network apart from a refusal', () => {
    expect(describeRemoveFailure(new TypeError('Failed to fetch'))).toBe(UNREACHABLE);
  });

  it('falls back rather than showing an empty sentence', () => {
    expect(describeRemoveFailure(undefined)).toBe(REMOVE_FALLBACK);
    expect(describeRemoveFailure({})).toBe(REMOVE_FALLBACK);
  });
});
