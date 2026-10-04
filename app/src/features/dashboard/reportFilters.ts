import type { CaseSummary } from '../cases/caseWorklistMapping';
import { matchesAdviser, matchesChecker, withinRange } from '../cases/worklistFilters';
import { checkerIdentity, type ContactEmails } from '../people/peopleDirectory';

/**
 * The filters the Dashboard and Management reporting share (2026-09-28): the worklist's
 * filters that make sense for totals. Status and outcome are left out on purpose - the
 * panels break those down, and filtering on them would empty the rest of each breakdown.
 *
 * Cases are filtered first; every other row (outcome, action, sign-off) is then narrowed to
 * those cases before it is counted, so each figure on the page answers the same question.
 */
export const REPORT_FILTER_KEYS = ['from', 'to', 'route', 'adviser', 'checker'] as const;

export type ReportFilterKey = (typeof REPORT_FILTER_KEYS)[number];

export type ReportFilters = Record<ReportFilterKey, string>;

export const EMPTY_REPORT_FILTERS = Object.fromEntries(
  REPORT_FILTER_KEYS.map((key) => [key, '']),
) as ReportFilters;

export function isFiltered(filters: ReportFilters): boolean {
  return REPORT_FILTER_KEYS.some((key) => filters[key] !== '');
}

/**
 * The ids of the cases the filters keep, or null when nothing is filtered.
 *
 * Null rather than every id, because an unfiltered page must count exactly what it counted
 * before filters existed - including an outcome or action row that names no case.
 */
export function casesInScope(
  cases: CaseSummary[],
  filters: ReportFilters,
  contactEmails: ContactEmails = new Map(),
): Set<string> | null {
  if (!isFiltered(filters)) return null;

  const ids = new Set<string>();
  for (const item of cases) {
    if (!withinRange(item.createdOn, filters.from, filters.to)) continue;
    if (filters.route === 'none' && item.route) continue;
    if (filters.route && filters.route !== 'none' && item.route !== filters.route) continue;
    if (filters.adviser && !matchesAdviser(item, filters.adviser)) continue;
    if (filters.checker && !matchesChecker(item, filters.checker, contactEmails)) continue;
    ids.add(item.id);
  }
  return ids;
}

/** Rows on a case in scope. A row naming no case is dropped once anything is filtered. */
export function narrowToScope<T>(
  rows: T[],
  scope: Set<string> | null,
  caseIdOf: (row: T) => string | undefined,
): T[] {
  if (!scope) return rows;
  return rows.filter((row) => {
    const id = caseIdOf(row);
    return id !== undefined && scope.has(id);
  });
}

/** A worklist link with the report's filters added, so a figure opens the list it counted. */
export function worklistLink(to: string, filters: ReportFilters): string {
  const [path, query = ''] = to.split('?');
  const params = new URLSearchParams(query);
  for (const key of REPORT_FILTER_KEYS) {
    if (filters[key]) params.set(key, filters[key]);
  }
  const text = params.toString();
  return text ? `${path}?${text}` : path;
}

/**
 * The adviser filter's options, keyed on email so two advisers of one name stay apart. The
 * label carries the name too, since the email alone cannot be matched back to a person by eye.
 */
export function adviserOptions(cases: CaseSummary[]): { value: string; label: string }[] {
  const byEmail = new Map<string, string>();
  for (const item of cases) {
    const email = item.adviserEmail?.trim().toLowerCase();
    if (email && !byEmail.has(email)) byEmail.set(email, item.adviser?.trim() || email);
  }
  return [...byEmail.entries()]
    .map(([value, name]) => ({ value, label: name === value ? value : `${name} (${value})` }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/**
 * The checker filter's options, keyed on the same identity `matchesChecker` now matches
 * against (item 1, 2026-10-04 review) - email where the directory has it, else contact id -
 * so two checkers who share a name stay apart as separate options rather than one the filter
 * cannot tell apart.
 */
export function checkerOptions(
  cases: CaseSummary[],
  contactEmails: ContactEmails = new Map(),
): { value: string; label: string }[] {
  const byIdentity = new Map<string, string>();
  for (const item of cases) {
    const entries: readonly [string | null, string | null][] = [
      [item.taxChecker, item.taxCheckerId],
      [item.aqsChecker, item.aqsCheckerId],
    ];
    for (const [name, contactId] of entries) {
      const trimmed = name?.trim();
      if (!trimmed) continue;
      const identity = checkerIdentity(trimmed, contactId, contactEmails);
      if (!byIdentity.has(identity)) byIdentity.set(identity, trimmed);
    }
  }
  return [...byIdentity.entries()]
    .map(([value, name]) => ({ value, label: name }))
    .sort((a, b) => a.label.localeCompare(b.label));
}
