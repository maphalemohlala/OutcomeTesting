import { Al_auditeventsal_command } from '../../generated/models/Al_auditeventsModel';
import { Al_questionversionsal_responsetype } from '../../generated/models/Al_questionversionsModel';
import { lookupLabel } from '../cases/lookupLabel';

/**
 * One question's history: what changed, when, and by whom (2026-09-28).
 *
 * Two sources, because neither holds the whole answer. The audit events say who and when
 * and why - every question write is a server-side command that writes one - but not what
 * changed: RetireAndSucceedQuestion records only "Superseded v1" and the new number. The
 * versions say what: an edit never overwrites, it dates the old version out and writes a new
 * one (FR-030, FR-031), and none is pruned. So consecutive versions are compared for the
 * change, and the event that made each one supplies who, when and why.
 *
 * Which event made which version, by what each command targets:
 *   AddQuestion, MoveQuestion  the question, with the new version's id in al_details
 *   RetireAndSucceedQuestion   the new version
 *   RetireQuestion             the version it dated out - a retirement, not a creation
 *
 * A version with no event (seeded by import, before the commands existed) falls back to the
 * row's own created-by and created-on, so the history still has a name and a date.
 */

/** Only what the history reads, so a test needs no generated model. */
export interface HistoryVersionSource {
  al_questionversionid: string;
  al_versionnumber: number;
  al_questiontext?: string;
  al_responsetype?: number;
  al_responsetypename?: string;
  al_ismandatory?: boolean;
  al_displayorder?: number;
  al_effectivefrom?: string;
  al_effectiveto?: string;
  createdon?: string;
  createdbyname?: string;
  modifiedon?: string;
  modifiedbyname?: string;
}

export interface HistoryAuditSource {
  al_auditeventid: string;
  al_command?: number;
  al_commandname?: string;
  al_targetid?: string;
  al_details?: string;
  al_name?: string;
  al_reason?: string;
  al_occurredon?: string;
  al_actorname?: string;
  createdon?: string;
  createdbyname?: string;
}

export interface HistoryChange {
  field: string;
  before: string;
  after: string;
}

export type HistoryAction = 'Added' | 'Moved here' | 'Edited' | 'Retired';

/** One thing that happened to a question or a section. Sections carry no version. */
export interface HistoryEntry {
  id: string;
  action: HistoryAction;
  version: number | null;
  /** The question's wording as it stood after this entry; empty for a section. */
  wording: string;
  when: string | null;
  who: string | null;
  reason: string | null;
  changes: HistoryChange[];
}

export type QuestionHistoryEntry = HistoryEntry;

export function command(event: HistoryAuditSource): string | undefined {
  return (
    event.al_commandname ??
    (event.al_command === undefined
      ? undefined
      : Al_auditeventsal_command[event.al_command as keyof typeof Al_auditeventsal_command])
  );
}

function responseType(version: HistoryVersionSource): string {
  return (
    version.al_responsetypename ??
    (version.al_responsetype === undefined
      ? undefined
      : Al_questionversionsal_responsetype[
          version.al_responsetype as keyof typeof Al_questionversionsal_responsetype
        ]) ??
    ''
  );
}

function yesNo(value: boolean | undefined): string {
  if (value === undefined || value === null) return '';
  return value ? 'Yes' : 'No';
}

/**
 * Who created or last modified a row. getAll leaves `createdbyname` and `modifiedbyname`
 * empty and puts the name on the lookup's formatted-value annotation, which is where
 * lookupLabel reads it for the worklist's owner too. Read from the field alone, every entry
 * on DEV said "Unknown person" (2026-09-28).
 */
export function person(record: object, attribute: 'createdby' | 'modifiedby', name?: string): string | null {
  return lookupLabel(record, attribute, name);
}

export function day(value: string | undefined): string {
  return value ? value.slice(0, 10) : '';
}

/**
 * The plug-ins narrate their own mechanics in the reason field on a succession. That is not
 * a reason anybody gave, and showing it beside every edit would read as though they had.
 */
export function givenReason(reason: string | undefined): string | null {
  const text = reason?.trim();
  if (!text || /^Superseded v\d+$/.test(text)) return null;
  return text;
}

function diff(before: HistoryVersionSource, after: HistoryVersionSource): HistoryChange[] {
  const fields: [string, string, string][] = [
    ['Wording', before.al_questiontext?.trim() ?? '', after.al_questiontext?.trim() ?? ''],
    ['Response type', responseType(before), responseType(after)],
    ['Mandatory', yesNo(before.al_ismandatory), yesNo(after.al_ismandatory)],
    [
      'Display order',
      before.al_displayorder === undefined || before.al_displayorder === null ? '' : String(before.al_displayorder),
      after.al_displayorder === undefined || after.al_displayorder === null ? '' : String(after.al_displayorder),
    ],
  ];
  return fields
    .filter(([, was, now]) => was !== now)
    .map(([field, was, now]) => ({ field, before: was, after: now }));
}

function movedCodes(event: HistoryAuditSource): HistoryChange[] {
  const match = /MoveQuestion\s+(\S+)\s*->\s*(\S+)/.exec(event.al_name ?? '');
  return match ? [{ field: 'Question code', before: match[1], after: match[2] }] : [];
}

/** Newest first. */
export function questionHistory(
  questionId: string,
  versions: HistoryVersionSource[],
  events: HistoryAuditSource[],
): QuestionHistoryEntry[] {
  const ordered = [...versions].sort((a, b) => a.al_versionnumber - b.al_versionnumber);
  // al_targetid and al_details are text columns, not lookups, so their GUIDs are not
  // guaranteed the lower case the SDK returns record ids in. Every id is compared folded.
  const key = (id: string | undefined) => (id ?? '').trim().toLowerCase();
  const versionIds = new Set(ordered.map((v) => key(v.al_questionversionid)));
  const questionKey = key(questionId);

  const madeBy = new Map<string, HistoryAuditSource>();
  const retiredBy = new Map<string, HistoryAuditSource>();
  for (const event of events) {
    const name = command(event);
    const target = key(event.al_targetid);
    const details = key(event.al_details);
    if (name === 'RetireQuestion' && versionIds.has(target)) {
      retiredBy.set(target, event);
    } else if (versionIds.has(target)) {
      madeBy.set(target, event);
    } else if (target === questionKey && versionIds.has(details)) {
      madeBy.set(details, event);
    }
  }

  // Built in the order things happened - version order is authoritative where two events
  // share a timestamp or one has none - then reversed.
  const entries: QuestionHistoryEntry[] = [];
  ordered.forEach((version, index) => {
    const event = madeBy.get(key(version.al_questionversionid));
    const moved = event && command(event) === 'MoveQuestion';
    const previous = index > 0 ? ordered[index - 1] : undefined;
    const wording = version.al_questiontext?.trim() ?? '';

    entries.push({
      id: version.al_questionversionid,
      action: previous ? 'Edited' : moved ? 'Moved here' : 'Added',
      version: version.al_versionnumber,
      wording,
      when: event?.al_occurredon ?? event?.createdon ?? version.createdon ?? null,
      who:
        event?.al_actorname?.trim() ||
        (event && person(event, 'createdby', event.createdbyname)) ||
        person(version, 'createdby', version.createdbyname),
      reason: givenReason(event?.al_reason),
      changes: previous ? diff(previous, version) : moved && event ? movedCodes(event) : [],
    });

    // A retirement is the last version dated out with nothing after it. A succeeded version
    // is dated out as well, but that is the edit above, not a retirement.
    const retirement = retiredBy.get(key(version.al_questionversionid));
    const isLast = index === ordered.length - 1;
    if (retirement || (isLast && version.al_effectiveto)) {
      const until = day(retirement?.al_details) || day(version.al_effectiveto);
      entries.push({
        id: `${version.al_questionversionid}:retired`,
        action: 'Retired',
        version: version.al_versionnumber,
        wording,
        when: retirement?.al_occurredon ?? retirement?.createdon ?? version.modifiedon ?? null,
        who:
          retirement?.al_actorname?.trim() ||
          (retirement && person(retirement, 'createdby', retirement.createdbyname)) ||
          person(version, 'modifiedby', version.modifiedbyname),
        reason: givenReason(retirement?.al_reason),
        changes: until ? [{ field: 'In force until', before: 'Open-ended', after: until }] : [],
      });
    }
  });

  return entries.reverse();
}
