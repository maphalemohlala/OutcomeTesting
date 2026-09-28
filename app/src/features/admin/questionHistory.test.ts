import { describe, expect, it } from 'vitest';
import {
  questionHistory,
  type HistoryAuditSource,
  type HistoryVersionSource,
} from './questionHistory';

function version(
  id: string,
  number: number,
  extra: Partial<HistoryVersionSource> = {},
): HistoryVersionSource {
  return {
    al_questionversionid: id,
    al_versionnumber: number,
    al_questiontext: 'Was the client’s attitude to risk recorded?',
    al_responsetypename: 'Pass/Fail',
    al_ismandatory: true,
    al_effectivefrom: '2026-08-01',
    createdon: '2026-08-01T09:00:00Z',
    createdbyname: 'Service Account',
    ...extra,
  };
}

function event(
  id: string,
  command: string,
  targetId: string,
  extra: Partial<HistoryAuditSource> = {},
): HistoryAuditSource {
  return {
    al_auditeventid: id,
    al_commandname: command,
    al_targetid: targetId,
    al_occurredon: '2026-09-20T10:00:00Z',
    al_actorname: 'T. Manager',
    ...extra,
  };
}

describe('questionHistory', () => {
  it('shows a rewording as the wording before and after, with who, when and why', () => {
    // The request of 2026-09-28: what changed, when, and by who. RetireAndSucceedQuestion's
    // own audit event records only "Superseded v1" and the new number, so the change itself
    // is read off the two versions, which are never pruned.
    const history = questionHistory(
      'q-1',
      [
        version('v-1', 1, { al_effectiveto: '2026-09-20' }),
        version('v-2', 2, {
          al_questiontext: 'Was the client’s attitude to risk recorded and evidenced?',
          al_effectivefrom: '2026-09-20',
          createdon: '2026-09-20T10:00:00Z',
        }),
      ],
      [
        event('e-2', 'RetireAndSucceedQuestion', 'v-2', {
          al_reason: 'Superseded v1',
          al_actorname: 'T. Manager',
        }),
      ],
    );

    expect(history[0]).toMatchObject({
      action: 'Edited',
      version: 2,
      who: 'T. Manager',
      when: '2026-09-20T10:00:00Z',
      changes: [
        {
          field: 'Wording',
          before: 'Was the client’s attitude to risk recorded?',
          after: 'Was the client’s attitude to risk recorded and evidenced?',
        },
      ],
    });
  });

  it('leaves out the bookkeeping reason a succession writes for itself', () => {
    // "Superseded v1" is the plug-in narrating its own mechanics, not a reason anybody gave.
    const history = questionHistory(
      'q-1',
      [version('v-1', 1), version('v-2', 2, { al_ismandatory: false })],
      [event('e-2', 'RetireAndSucceedQuestion', 'v-2', { al_reason: 'Superseded v1' })],
    );

    expect(history[0].reason).toBeNull();
  });

  it('lists every field that changed in one edit', () => {
    const history = questionHistory(
      'q-1',
      [
        version('v-1', 1),
        version('v-2', 2, { al_responsetypename: 'Yes/No', al_ismandatory: false }),
      ],
      [],
    );

    expect(history[0].changes).toEqual([
      { field: 'Response type', before: 'Pass/Fail', after: 'Yes/No' },
      { field: 'Mandatory', before: 'Yes', after: 'No' },
    ]);
  });

  it('falls back to who created the version when no audit event names them', () => {
    // Seeded versions were written by import, not by a command, so they carry no event.
    const history = questionHistory('q-1', [version('v-1', 1)], []);

    expect(history).toEqual([
      expect.objectContaining({
        action: 'Added',
        version: 1,
        who: 'Service Account',
        when: '2026-08-01T09:00:00Z',
      }),
    ]);
  });

  it('reads the addition from the event that targets the question, not the version', () => {
    // AddQuestion targets the question and carries the version id in its details.
    const history = questionHistory(
      'q-1',
      [version('v-1', 1)],
      [
        event('e-1', 'AddQuestion', 'q-1', {
          al_details: 'v-1',
          al_reason: 'Added to section Suitability',
          al_actorname: 'A. Admin',
        }),
      ],
    );

    expect(history[0]).toMatchObject({
      action: 'Added',
      who: 'A. Admin',
      reason: 'Added to section Suitability',
    });
  });

  it('records a move as where it came from', () => {
    const history = questionHistory(
      'q-2',
      [version('v-9', 1)],
      [
        event('e-9', 'MoveQuestion', 'q-2', {
          al_details: 'v-9',
          al_name: 'MoveQuestion Q-SUIT-04 -> Q-KYC-07',
          al_reason: 'Belongs with KYC (moved to Know your client)',
        }),
      ],
    );

    expect(history[0]).toMatchObject({
      action: 'Moved here',
      reason: 'Belongs with KYC (moved to Know your client)',
      changes: [{ field: 'Question code', before: 'Q-SUIT-04', after: 'Q-KYC-07' }],
    });
  });

  it('shows a retirement as its own entry, newest first', () => {
    const history = questionHistory(
      'q-1',
      [version('v-1', 1, { al_effectiveto: '2026-09-25' })],
      [
        event('e-r', 'RetireQuestion', 'v-1', {
          al_occurredon: '2026-09-25T08:00:00Z',
          al_reason: 'No longer asked',
          al_details: '2026-09-25',
        }),
      ],
    );

    expect(history.map((entry) => entry.action)).toEqual(['Retired', 'Added']);
    expect(history[0]).toMatchObject({
      reason: 'No longer asked',
      changes: [{ field: 'In force until', before: 'Open-ended', after: '2026-09-25' }],
    });
  });

  it('does not report a retirement for a version that was succeeded', () => {
    // A succession dates the old version out too; that is the edit, not a retirement.
    const history = questionHistory(
      'q-1',
      [version('v-1', 1, { al_effectiveto: '2026-09-20' }), version('v-2', 2, { al_ismandatory: false })],
      [],
    );

    expect(history.map((entry) => entry.action)).toEqual(['Edited', 'Added']);
  });

  it('matches an event whose target id is in another case', () => {
    // al_targetid is a text column, not a lookup, so nothing makes its GUID lower case the
    // way the SDK returns record ids. A mismatch would silently drop the name and the reason.
    const history = questionHistory(
      'q-1',
      [version('v-1', 1), version('abc-2', 2, { al_ismandatory: false })],
      [event('e-2', 'RetireAndSucceedQuestion', 'ABC-2', { al_actorname: 'T. Manager' })],
    );

    expect(history[0].who).toBe('T. Manager');
  });

  it('reads the creator from the formatted-value annotation when no name field is sent', () => {
    // Found on DEV, 2026-09-28: every entry read "Unknown person". getAll leaves createdbyname
    // empty and puts the name on the lookup's annotation instead - the same thing lookupLabel
    // handles for the worklist's owner.
    const seeded = {
      ...version('v-1', 1, { createdbyname: undefined }),
      '_createdby_value@OData.Community.Display.V1.FormattedValue': 'svc automate aq',
    };

    expect(questionHistory('q-1', [seeded], [])[0].who).toBe('svc automate aq');
  });
});
