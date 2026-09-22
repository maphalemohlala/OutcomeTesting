/**
 * Whether a person's held roles satisfy the chosen filter.
 *
 * A person may hold several roles, so this asks whether they hold the chosen one at all
 * rather than whether it is their only one. Compared case-insensitively and trimmed,
 * because role mappings are keyed on a hand-entered email and carry a hand-entered label.
 *
 * Both sides are Power Pages web role NAMES. There is no second vocabulary to translate
 * between: `al_rolecode` carries the web role's name, `useRoles` reads the same names off
 * `mspp_webrole`, and the Role column displays them unchanged.
 *
 * This is worth stating because it was wrong until 2026-09-22. The filter offered the six
 * `al_Role` labels from `data/roles-seed` - Adviser, Paraplanner, T&C Manager and the three
 * Checkers - and compared them against web role names like "AL Portal - Planner". The two
 * lists share no member, so every specific role matched nobody and the filter read "0 of 12
 * people" whatever was chosen. `al_role` is retired: OD-037 recorded on 2026-09-08 that no
 * `ROLE-*` code appears in `al_userrolemapping` or `al_pagepermission`, and dropping the
 * last read of it is what allows the table to be deleted.
 */
export function matchesRole(roles: readonly string[], filter: string): boolean {
  if (filter === 'all') return true;
  const wanted = filter.trim().toLowerCase();
  return roles.some((role) => role.trim().toLowerCase() === wanted);
}
