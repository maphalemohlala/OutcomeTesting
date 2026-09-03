import type { Outcome } from '../../types/domain';
import { OUTCOMES } from '../../types/domain';
import type { CaseSummary } from '../cases/caseWorklistMapping';

/**
 * The four ways a person appears on a case. These are positions on the case record, not
 * security roles: `al_OutcomeCase` carries adviser, paraplanner and checker as names from
 * the Intelligent Office extract, and the owner is the Dataverse user the case is
 * allocated to (BR-003). No approved requirement joins those names to the `al_User`
 * registry — that key is work email (AD-010), which the case does not carry — so this
 * view groups by the name as recorded and does not claim to be a user directory.
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

export function personKey(role: PersonRole, name: string): string {
  return `${role}:${name.toLowerCase()}`;
}

function positions(item: CaseSummary): { role: PersonRole; name: string | null; code: string | null }[] {
  return [
    { role: 'Adviser', name: item.adviser, code: item.adviserCode },
    { role: 'Paraplanner', name: item.paraplanner, code: item.paraplannerCode },
    { role: 'Checker', name: item.checker, code: null },
    { role: 'Owner', name: item.owner, code: null },
  ];
}

function emptyOutcomes(): Record<Outcome, number> {
  return Object.fromEntries(OUTCOMES.map((outcome) => [outcome, 0])) as Record<Outcome, number>;
}

const CLOSED_STATUSES = new Set(['Closed', 'No Check Required']);

/** One row per person per position, so someone who both checks and advises shows as both. */
export function buildDirectory(cases: CaseSummary[]): PersonSummary[] {
  const people = new Map<string, PersonSummary>();

  for (const item of cases) {
    for (const position of positions(item)) {
      const name = position.name?.trim();
      if (!name) continue;

      const key = personKey(position.role, name);
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
  name: string,
): CaseSummary[] {
  const target = name.trim().toLowerCase();
  return cases.filter((item) =>
    positions(item).some(
      (position) => position.role === role && (position.name ?? '').trim().toLowerCase() === target,
    ),
  );
}

export function isPersonRole(value: string | undefined): value is PersonRole {
  return (PERSON_ROLES as readonly string[]).includes(value ?? '');
}
