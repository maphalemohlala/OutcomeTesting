import {
  command,
  day,
  givenReason,
  person,
  type HistoryAuditSource,
  type HistoryChange,
  type HistoryEntry,
} from './questionHistory';

/**
 * One section's history: what changed, when, and by whom (2026-09-28).
 *
 * Unlike a question, a section is edited in place - there is no earlier version to compare
 * against - so the audit event is the only record of what it said before. That is enough:
 * UpdateSectionPlugin.DescribeChanges writes the before and after of every changed field into
 * al_details, one clause per field, and parseSectionChanges reads them back.
 *
 * Every section command targets the section itself (AddSection, UpdateSection, RetireSection),
 * so the events are simply those whose target is this section. A section seeded by import
 * carries no AddSection event, and falls back to the row's own created-by and created-on.
 */

/** Only what the history reads, so a test needs no generated model. */
export interface HistorySectionSource {
  al_sectionid: string;
  al_effectiveto?: string | null;
  createdon?: string;
  createdbyname?: string;
  modifiedon?: string;
  modifiedbyname?: string;
}

/** The field names DescribeChanges writes, in the order it writes them. */
const SECTION_FIELDS = ['Name', 'Help text', 'Owner role', 'Display order', 'Optional'];

/**
 * The clauses DescribeChanges writes: `Name: 'before' -> 'after'`, joined with "; ".
 *
 * Matched field by known field, not split on "; " or on quotes, because help text is free
 * text and may contain either. A clause ends only where the next known field begins.
 */
export function parseSectionChanges(details: string | undefined): HistoryChange[] {
  if (!details) return [];
  const names = SECTION_FIELDS.join('|');
  const clause = new RegExp(
    `(${names}): '([\\s\\S]*?)' -> '([\\s\\S]*?)'(?=; (?:${names}): '|$)`,
    'g',
  );
  return [...details.matchAll(clause)].map((match) => ({
    field: match[1],
    before: match[2],
    after: match[3],
  }));
}

function retiredChange(until: string): HistoryChange[] {
  return until ? [{ field: 'In force until', before: 'Open-ended', after: until }] : [];
}

/** Newest first. */
export function sectionHistory(section: HistorySectionSource, events: HistoryAuditSource[]): HistoryEntry[] {
  const key = (id: string | undefined) => (id ?? '').trim().toLowerCase();
  const own = events
    .filter((event) => key(event.al_targetid) === key(section.al_sectionid))
    .map((event) => ({ event, name: command(event), when: event.al_occurredon ?? event.createdon ?? null }))
    .sort((a, b) => (a.when ?? '').localeCompare(b.when ?? ''));

  const entries: HistoryEntry[] = [];

  if (!own.some(({ name }) => name === 'AddSection')) {
    entries.push({
      id: `${section.al_sectionid}:created`,
      action: 'Added',
      version: null,
      wording: '',
      when: section.createdon ?? null,
      who: person(section, 'createdby', section.createdbyname),
      reason: null,
      changes: [],
    });
  }

  for (const { event, name, when } of own) {
    const who = event.al_actorname?.trim() || person(event, 'createdby', event.createdbyname);
    const base = { id: event.al_auditeventid, version: null, wording: '', when, who, reason: givenReason(event.al_reason) };
    if (name === 'AddSection') entries.push({ ...base, action: 'Added', changes: [] });
    else if (name === 'UpdateSection') {
      entries.push({ ...base, action: 'Edited', changes: parseSectionChanges(event.al_details) });
    } else if (name === 'RetireSection') {
      entries.push({ ...base, action: 'Retired', changes: retiredChange(day(event.al_details)) });
    }
  }

  if (section.al_effectiveto && !own.some(({ name }) => name === 'RetireSection')) {
    entries.push({
      id: `${section.al_sectionid}:retired`,
      action: 'Retired',
      version: null,
      wording: '',
      when: section.modifiedon ?? null,
      who: person(section, 'modifiedby', section.modifiedbyname),
      reason: null,
      changes: retiredChange(day(section.al_effectiveto)),
    });
  }

  return entries.reverse();
}
