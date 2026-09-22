/**
 * The roles the People page filters by (project owner, 2026-09-22).
 *
 * These are the roles `al_Role` is seeded with that describe what a person DOES. The three
 * checker roles stay distinct rather than collapsing into one "Checker": that is how they
 * are seeded, and the Tax/AQS review routing already tells them apart, so grouping them
 * here would put a distinction the system relies on behind a label that hides it.
 *
 * Administrator, Outcome Testing Manager, Reviewer and Read Only User are deliberately
 * absent — they describe access to this application rather than a job on a case.
 */
export const ROLE_FILTERS = [
  'Adviser',
  'Paraplanner',
  'T&C Manager',
  'Tax Checker',
  'AQS Checker',
  'Senior Checker',
] as const;

/**
 * Whether a person's held roles satisfy the chosen filter.
 *
 * A person may hold several roles, so this asks whether they hold the chosen one at all
 * rather than whether it is their only one. Compared case-insensitively and trimmed,
 * because role mappings are keyed on a hand-entered email and carry a hand-entered label.
 */
export function matchesRole(roles: readonly string[], filter: string): boolean {
  if (filter === 'all') return true;
  const wanted = filter.trim().toLowerCase();
  return roles.some((role) => role.trim().toLowerCase() === wanted);
}
