import type { CaseSummary } from './caseWorklistMapping';

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

function matchesPerson(item: CaseSummary, person: string): boolean {
  const name = person.toLowerCase();
  // Both checkers, so a search for a name finds the case whichever discipline that
  // person holds (item 2, 2026-09-19). Searching one column used to miss the other.
  return [item.adviser, item.paraplanner, item.taxChecker, item.aqsChecker, item.owner].some(
    (value) => (value ?? '').toLowerCase() === name,
  );
}

export function matchesChecker(item: CaseSummary, checker: string): boolean {
  return item.taxChecker === checker || item.aqsChecker === checker;
}

function matchesRemediation(item: CaseSummary, remediation: string): boolean {
  if (remediation === 'none') return !item.remediation;
  return item.remediation?.state === remediation;
}

export function applyFilters(cases: CaseSummary[], filters: Filters): CaseSummary[] {
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
    if (filters.person && !matchesPerson(item, filters.person)) return false;
    if (filters.adviser && item.adviser !== filters.adviser) return false;
    if (filters.checker && !matchesChecker(item, filters.checker)) return false;
    if (!withinRange(item.createdOn, filters.from, filters.to)) return false;
    if (search) {
      const haystack =
        `${item.caseReference} ${item.owner ?? ''} ${item.client ?? ''} ${item.adviser ?? ''}`.toLowerCase();
      if (!haystack.includes(search)) return false;
    }
    return true;
  });
}
