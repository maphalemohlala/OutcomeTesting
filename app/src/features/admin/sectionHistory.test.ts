import { describe, expect, it } from 'vitest';
import type { HistoryAuditSource } from './questionHistory';
import { parseSectionChanges, sectionHistory, type HistorySectionSource } from './sectionHistory';

const SECTION: HistorySectionSource = {
  al_sectionid: 's-1',
  createdon: '2026-08-01T09:00:00Z',
  createdbyname: 'Service Account',
};

function event(
  id: string,
  command: string,
  extra: Partial<HistoryAuditSource> = {},
): HistoryAuditSource {
  return {
    al_auditeventid: id,
    al_commandname: command,
    al_targetid: 's-1',
    al_occurredon: '2026-09-20T10:00:00Z',
    al_actorname: 'T. Manager',
    ...extra,
  };
}

describe('parseSectionChanges', () => {
  it('reads the before and after UpdateSectionPlugin.DescribeChanges writes', () => {
    // A section is edited in place, not versioned, so this text is the only record of what
    // it said before. The plug-in writes one clause per changed field.
    expect(
      parseSectionChanges("Name: 'Suitability' -> 'Suitability of advice'; Optional: 'No' -> 'Yes'"),
    ).toEqual([
      { field: 'Name', before: 'Suitability', after: 'Suitability of advice' },
      { field: 'Optional', before: 'No', after: 'Yes' },
    ]);
  });

  it('keeps a value that itself contains a quote and a semicolon', () => {
    // Help text is free text. Splitting on "; " or on quotes would cut it in two.
    expect(
      parseSectionChanges("Help text: '' -> 'Check the client's goals; then the risk'; Display order: '2' -> '3'"),
    ).toEqual([
      { field: 'Help text', before: '', after: "Check the client's goals; then the risk" },
      { field: 'Display order', before: '2', after: '3' },
    ]);
  });

  it('returns nothing for details it does not recognise', () => {
    expect(parseSectionChanges('2026-09-25')).toEqual([]);
    expect(parseSectionChanges(undefined)).toEqual([]);
  });
});

describe('sectionHistory', () => {
  it('lists the section’s own events newest first, with who, when and why', () => {
    const history = sectionHistory(SECTION, [
      event('e-1', 'AddSection', {
        al_occurredon: '2026-09-01T09:00:00Z',
        al_reason: 'Owner role AQS, 3 question(s)',
        al_actorname: 'A. Admin',
      }),
      event('e-2', 'UpdateSection', {
        al_occurredon: '2026-09-10T09:00:00Z',
        al_reason: 'Renamed for the handbook',
        al_details: "Name: 'Suitability' -> 'Suitability of advice'",
      }),
      event('e-3', 'RetireSection', {
        al_occurredon: '2026-09-20T09:00:00Z',
        al_reason: 'Merged into KYC',
        al_details: '2026-09-21',
      }),
    ]);

    expect(history.map((entry) => entry.action)).toEqual(['Retired', 'Edited', 'Added']);
    expect(history[0]).toMatchObject({
      who: 'T. Manager',
      reason: 'Merged into KYC',
      changes: [{ field: 'In force until', before: 'Open-ended', after: '2026-09-21' }],
    });
    expect(history[1].changes).toEqual([
      { field: 'Name', before: 'Suitability', after: 'Suitability of advice' },
    ]);
    expect(history[2]).toMatchObject({ who: 'A. Admin', reason: 'Owner role AQS, 3 question(s)' });
  });

  it('falls back to the row for a seeded section that no command added', () => {
    const history = sectionHistory(SECTION, []);

    expect(history).toEqual([
      expect.objectContaining({ action: 'Added', who: 'Service Account', when: '2026-08-01T09:00:00Z' }),
    ]);
  });

  it('shows a retirement the row records even where no event does', () => {
    const history = sectionHistory(
      { ...SECTION, al_effectiveto: '2026-09-25', modifiedon: '2026-09-24T12:00:00Z', modifiedbyname: 'B. Admin' },
      [],
    );

    expect(history[0]).toMatchObject({
      action: 'Retired',
      who: 'B. Admin',
      changes: [{ field: 'In force until', before: 'Open-ended', after: '2026-09-25' }],
    });
  });

  it('ignores events that belong to other sections', () => {
    const history = sectionHistory(SECTION, [event('e-x', 'UpdateSection', { al_targetid: 's-2' })]);

    expect(history.map((entry) => entry.action)).toEqual(['Added']);
  });

  it('reads who retired it from the modified-by annotation when no name field is sent', () => {
    const history = sectionHistory(
      {
        ...SECTION,
        al_effectiveto: '2026-09-25',
        '_modifiedby_value@OData.Community.Display.V1.FormattedValue': 'B. Admin',
      } as HistorySectionSource,
      [],
    );

    expect(history[0].who).toBe('B. Admin');
  });
});
