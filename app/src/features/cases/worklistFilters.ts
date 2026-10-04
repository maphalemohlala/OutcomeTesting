import type { CaseSummary } from './caseWorklistMapping';
import { isEmail } from './casePeople';
import {
  checkerIdentity,
  identityOf,
  isIdentityKey,
  positions,
  type ContactEmails,
} from '../people/peopleDirectory';

/**
 * The worklist's filters, kept out of the page so they can be tested.
 *
 * Filters live in the URL so a dashboard card or a person view can link straight to the
 * list it counted, and so the view a manager exports is the view they can share back.
 * `adviser` and `checker` have no control on the worklist itself: they arrive from a
 * filtered dashboard figure, which has to open the list that figure counted.
 */
export const FILTER_KEYS = [
  'q',
  'status',
  'route',
  'priority',
  'outcome',
  'remediation',
  'person',
  'adviser',
  'checker',
  'from',
  'to',
] as const;

export type FilterKey = (typeof FILTER_KEYS)[number];

export type Filters = Record<FilterKey, string>;

export const EMPTY_FILTERS = Object.fromEntries(FILTER_KEYS.map((key) => [key, ''])) as Filters;

export function withinRange(createdOn: string | null, from: string, to: string): boolean {
  if (!from && !to) return true;
  if (!createdOn) return false;
  const created = createdOn.slice(0, 10);
  if (from && created < from) return false;
  if (to && created > to) return false;
  return true;
}

function matchesPerson(item: CaseSummary, person: string, contactEmails: ContactEmails): boolean {
  const target = person.trim().toLowerCase();
  // Every position, so a search finds the case whichever one that person holds (item 2,
  // 2026-09-19). Searching one column used to miss the others.
  return positions(item).some((position) => identityOf(position, contactEmails) === target);
}

/**
 * Keyed on the same identity the People page uses for checkers (item 1, 2026-10-04 review):
 * `checkerIdentity`, email where the directory has it, else contact id. Two checkers who
 * share a name stay apart once the filter value is one of those identities.
 *
 * A value with no `email:`/`contact:`/`name:` prefix is a legacy bookmark or saved filter
 * from before checkers were keyed this way, and is matched by name exactly as it always was
 * - so it keeps working rather than silently matching nobody.
 */
export function matchesChecker(
  item: CaseSummary,
  checker: string,
  contactEmails: ContactEmails = new Map(),
): boolean {
  if (isIdentityKey(checker)) {
    const target = checker.trim().toLowerCase();
    return (
      checkerIdentity(item.taxChecker, item.taxCheckerId, contactEmails) === target ||
      checkerIdentity(item.aqsChecker, item.aqsCheckerId, contactEmails) === target
    );
  }
  return item.taxChecker === checker || item.aqsChecker === checker;
}

/**
 * The adviser filter's own legacy fallback (item 2, 2026-10-04 review). The current scheme
 * keys this filter on the plain adviser email (`adviserOptions`), so a value that is not
 * shaped like an email is an old saved filter from before advisers were keyed by email at
 * all, and is matched by name instead of silently matching nobody.
 */
export function matchesAdviser(item: CaseSummary, adviser: string): boolean {
  const target = adviser.trim().toLowerCase();
  if (isEmail(target)) {
    return (item.adviserEmail ?? '').trim().toLowerCase() === target;
  }
  return (item.adviser ?? '').trim().toLowerCase() === target;
}

function matchesRemediation(item: CaseSummary, remediation: string): boolean {
  if (remediation === 'none') return !item.remediation;
  return item.remediation?.state === remediation;
}

export function applyFilters(
  cases: CaseSummary[],
  filters: Filters,
  contactEmails: ContactEmails = new Map(),
): CaseSummary[] {
  const search = filters.q.trim().toLowerCase();
  return cases.filter((item) => {
    if (filters.status && item.status !== filters.status) return false;
    if (filters.route === 'none' && item.route) return false;
    if (filters.route && filters.route !== 'none' && item.route !== filters.route) {
      return false;
    }
    if (filters.priority && (item.priority ?? '') !== filters.priority) return false;
    // "Not yet graded" is what the Outcome cell says, and that cell shows the Tax grade where
    // a case has no BR-005 outcome (AD-055), so a Tax-graded case is not ungraded.
    if (filters.outcome === 'none' && (item.latestOutcome || item.taxOutcome)) return false;
    if (filters.outcome && filters.outcome !== 'none' && item.latestOutcome !== filters.outcome) {
      return false;
    }
    if (filters.remediation && !matchesRemediation(item, filters.remediation)) return false;
    if (filters.person && !matchesPerson(item, filters.person, contactEmails)) return false;
    if (filters.adviser && !matchesAdviser(item, filters.adviser)) return false;
    if (filters.checker && !matchesChecker(item, filters.checker, contactEmails)) return false;
    if (!withinRange(item.createdOn, filters.from, filters.to)) return false;
    if (search) {
      const haystack =
        `${item.caseReference} ${item.owner ?? ''} ${item.client ?? ''} ${item.adviser ?? ''}`.toLowerCase();
      if (!haystack.includes(search)) return false;
    }
    return true;
  });
}
