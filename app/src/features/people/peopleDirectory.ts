import type { Outcome } from '../../types/domain';
import { OUTCOMES } from '../../types/domain';
import type { CaseSummary } from '../cases/caseWorklistMapping';

/**
 * The four ways a person appears on a case. These are positions on the case record, not
 * security roles: `al_OutcomeCase` carries adviser, paraplanner and checker, and the owner
 * is the Dataverse user the case is allocated to (BR-003). Adviser and paraplanner are keyed
 * by the email the case stores; checkers by their contact, through the directory's email.
 */
export const PERSON_ROLES = ['Adviser', 'Paraplanner', 'Checker', 'Owner'] as const;

export type PersonRole = (typeof PERSON_ROLES)[number];

export interface PersonSummary {
  key: string;
  role: PersonRole;
  name: string;
  code: string | null;
  totalCases: number;
  openCases: number;
  closedCases: number;
  notGraded: number;
  outcomes: Record<Outcome, number>;
  oldestOpenDays: number;
}

export function personKey(role: PersonRole, identity: string): string {
  return `${role}:${identity.toLowerCase()}`;
}

export type ContactEmails = ReadonlyMap<string, string>;

export function contactEmailsOf(users: { id: string; email: string }[]): ContactEmails {
  return new Map(
    users
      .filter((u) => u.email.trim() !== '')
      .map((u) => [u.id.toLowerCase(), u.email.trim().toLowerCase()] as const),
  );
}

/**
 * Who a position is. The email wherever it is known - stored on the case for the adviser
 * and paraplanner, through the directory for a checker - so two people of one name stay two
 * people and one person in two positions stays one. A name only for a legacy row with nothing
 * better, marked as such so it never merges with an email-keyed person.
 */
export function identityOf(
  position: { name: string | null; email: string | null; contactId: string | null },
  contactEmails: ContactEmails = new Map(),
): string {
  const email = position.email?.trim().toLowerCase();
  if (email) return `email:${email}`;
  const id = position.contactId?.trim().toLowerCase();
  if (id) {
    const viaDirectory = contactEmails.get(id);
    return viaDirectory ? `email:${viaDirectory}` : `contact:${id}`;
  }
  return `name:${(position.name ?? '').trim().toLowerCase()}`;
}

/**
 * The email an `email:`-prefixed identity carries, or '' for a `contact:` or `name:` one
 * (F6 of the 2026-10-04 final review). The People page's unregistered rows show it, so a
 * person the directory does not hold is still shown by the email the case actually
 * identifies them by, not a blank.
 */
export function emailFromIdentity(identity: string): string {
  return identity.startsWith('email:') ? identity.slice('email:'.length) : '';
}

export function adviserIdentity(item: CaseSummary): string {
  return identityOf({ name: item.adviser, email: item.adviserEmail, contactId: null });
}

/**
 * A checker's identity, for the one-cell-at-a-time callers (the worklist's Tax/AQS checker
 * columns) that do not have a whole `CaseSummary` to read `positions()` from. Delegates to
 * `identityOf` so there remains one definition of what a checker's identity is.
 */
export function checkerIdentity(
  name: string | null,
  contactId: string | null,
  contactEmails: ContactEmails = new Map(),
): string {
  return identityOf({ name, email: null, contactId }, contactEmails);
}

export interface Position {
  role: PersonRole;
  name: string | null;
  code: string | null;
  email: string | null;
  contactId: string | null;
}

export function positions(item: CaseSummary): Position[] {
  return [
    { role: 'Adviser', name: item.adviser, code: item.adviserCode, email: item.adviserEmail, contactId: null },
    {
      role: 'Paraplanner',
      name: item.paraplanner,
      code: item.paraplannerCode,
      email: item.paraplannerEmail,
      contactId: null,
    },
    ...checkers(item),
    { role: 'Owner', name: item.owner, code: null, email: null, contactId: null },
  ];
}

/**
 * The case's checkers, as directory positions (item 2, 2026-09-19).
 *
 * The role stays "Checker" rather than splitting into Tax and AQS: this directory answers
 * "what has this person got on", and someone who checks both disciplines is one person with
 * one workload, not two entries to be read side by side.
 *
 * De-duplicated by contact id where present - that is what stops a case counting twice
 * against someone who holds BOTH of its checks, the case would otherwise be added to their
 * total once per column. Falls back to name when neither carries a contact id. Two distinct
 * people produce two positions, which is correct: that case really is on two people's desks.
 */
function checkers(item: CaseSummary): Position[] {
  const entries = [
    { name: item.taxChecker, contactId: item.taxCheckerId },
    { name: item.aqsChecker, contactId: item.aqsCheckerId },
  ]
    .map((entry) => ({ name: entry.name?.trim() ?? '', contactId: entry.contactId?.trim() ?? null }))
    .filter((entry) => entry.name !== '');

  const distinct = new Map<string, { name: string; contactId: string | null }>();
  for (const entry of entries) {
    const dedupeKey = entry.contactId ? `id:${entry.contactId.toLowerCase()}` : `name:${entry.name.toLowerCase()}`;
    if (!distinct.has(dedupeKey)) distinct.set(dedupeKey, entry);
  }

  return [...distinct.values()].map((entry) => ({
    role: 'Checker' as PersonRole,
    name: entry.name,
    code: null,
    email: null,
    contactId: entry.contactId,
  }));
}

function emptyOutcomes(): Record<Outcome, number> {
  return Object.fromEntries(OUTCOMES.map((outcome) => [outcome, 0])) as Record<Outcome, number>;
}

const CLOSED_STATUSES = new Set(['Closed', 'No Check Required']);

/** One row per person per position, so someone who both checks and advises shows as both. */
export function buildDirectory(
  cases: CaseSummary[],
  contactEmails: ContactEmails = new Map(),
): PersonSummary[] {
  const people = new Map<string, PersonSummary>();

  for (const item of cases) {
    for (const position of positions(item)) {
      const identity = identityOf(position, contactEmails);
      if (identity === 'name:') continue;

      const name = (position.name ?? '').trim();
      const key = personKey(position.role, identity);
      const person =
        people.get(key) ??
        ({
          key,
          role: position.role,
          name,
          code: null,
          totalCases: 0,
          openCases: 0,
          closedCases: 0,
          notGraded: 0,
          outcomes: emptyOutcomes(),
          oldestOpenDays: 0,
        } satisfies PersonSummary);

      person.totalCases += 1;
      person.code = person.code ?? position.code ?? null;

      if (CLOSED_STATUSES.has(item.status)) {
        person.closedCases += 1;
      } else {
        person.openCases += 1;
        if (item.ageInDays > person.oldestOpenDays) person.oldestOpenDays = item.ageInDays;
      }

      if (item.latestOutcome) person.outcomes[item.latestOutcome] += 1;
      else person.notGraded += 1;

      people.set(key, person);
    }
  }

  return [...people.values()].sort(
    (a, b) => b.totalCases - a.totalCases || a.name.localeCompare(b.name),
  );
}

export function casesForPerson(
  cases: CaseSummary[],
  role: PersonRole,
  identity: string,
  contactEmails: ContactEmails = new Map(),
): CaseSummary[] {
  const target = identity.toLowerCase();
  return cases.filter((item) =>
    positions(item).some(
      (position) => position.role === role && identityOf(position, contactEmails) === target,
    ),
  );
}

export function isPersonRole(value: string | undefined): value is PersonRole {
  return (PERSON_ROLES as readonly string[]).includes(value ?? '');
}

/**
 * One person's whole caseload, across every position they hold.
 *
 * `buildDirectory` deliberately emits a row per person *per position*, because that is
 * how a workload is read. Joining to the registry needs the opposite: one row per human,
 * keyed on something the registry also has. The email wherever it is known is that key -
 * stored on the case for the adviser and paraplanner, through the directory for a checker -
 * so two people of one name stay two people and one person in two positions stays one. A
 * name only for a legacy row with nothing better.
 */
export interface PersonCaseload {
  /** As recorded on the case, for display. */
  name: string;
  /** The key this caseload is filed under: `email:…`, `contact:…` or `name:…`. */
  identity: string;
  roles: PersonRole[];
  code: string | null;
  totalCases: number;
  openCases: number;
  closedCases: number;
  notGraded: number;
  outcomes: Record<Outcome, number>;
  oldestOpenDays: number;
}

export function caseloadByIdentity(
  cases: CaseSummary[],
  contactEmails: ContactEmails = new Map(),
): Map<string, PersonCaseload> {
  const loads = new Map<string, PersonCaseload>();
  // A person can hold two positions on one case — adviser and checker, say. Counting the
  // case once per position would inflate their load, so each case is counted once per
  // person and the positions are collected alongside.
  const seen = new Map<string, Set<string>>();

  for (const item of cases) {
    for (const position of positions(item)) {
      const key = identityOf(position, contactEmails);
      if (key === 'name:') continue;

      const name = (position.name ?? '').trim();
      const load =
        loads.get(key) ??
        ({
          name,
          identity: key,
          roles: [],
          code: null,
          totalCases: 0,
          openCases: 0,
          closedCases: 0,
          notGraded: 0,
          outcomes: emptyOutcomes(),
          oldestOpenDays: 0,
        } satisfies PersonCaseload);

      if (!load.roles.includes(position.role)) load.roles.push(position.role);
      load.code = load.code ?? position.code ?? null;

      const counted = seen.get(key) ?? new Set<string>();
      if (!counted.has(item.id)) {
        counted.add(item.id);
        seen.set(key, counted);

        load.totalCases += 1;
        if (CLOSED_STATUSES.has(item.status)) {
          load.closedCases += 1;
        } else {
          load.openCases += 1;
          if (item.ageInDays > load.oldestOpenDays) load.oldestOpenDays = item.ageInDays;
        }

        if (item.latestOutcome) load.outcomes[item.latestOutcome] += 1;
        else load.notGraded += 1;
      }

      loads.set(key, load);
    }
  }

  return loads;
}
