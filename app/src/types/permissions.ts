/*
 * Application RBAC model (AD-041). This is the single client-side source of truth
 * for what a role may see and do; the server-side Custom API commands (AD-003)
 * enforce the same rules against Dataverse privileges, so the app layer is
 * advisory only and never the security boundary (AGENTS.md invariant).
 *
 * Roles trace to AD-020/AD-031. Resource keys and access levels are the AD-041
 * vocabulary. Do not add a role or capability here without a requirement/AD ID.
 */

/**
 * App roles are the Power Pages web roles (AD-041, AD-044). The list here is the
 * vocabulary the permission matrix is written against; the live list the app offers comes
 * from mspp_webrole, so a role added on the portal appears without a code change.
 *
 * These names travel through al_rolecode — the free-text column AD-044 reserved for custom
 * roles, which already outranks the al_approle picklist — so nothing in the schema changed
 * to support them.
 */
export const APP_ROLES = [
  'AL Portal - Tax Reviewer',
  'AL Portal - AQS Reviewer',
  'AL Portal - Adviser Remediation',
  'AL Portal - T&C Supervisor',
  'AL Portal - Outcome Testing Manager',
  'AL Portal - Planner',
  'AL Portal - Portal Administrator',
  'Administrators',
  // AD-218: the two team managers, who allocate and oversee their own discipline only.
  'AL Portal - Tax Team Manager',
  'AL Portal - AQS Team Manager',
] as const;

export type AppRole = (typeof APP_ROLES)[number];

/**
 * Web roles that exist to make Power Pages work rather than to describe a job. Excluded
 * from the app's role list: granting business access to "everyone who is signed in" is not
 * a decision any requirement makes.
 */
export const SYSTEM_WEB_ROLES: readonly string[] = ['Anonymous Users', 'Authenticated Users'];

export function isSystemWebRole(name: string): boolean {
  return SYSTEM_WEB_ROLES.some((role) => role.toLowerCase() === name.trim().toLowerCase());
}

/**
 * The six role names the al_approle picklist holds (AD-041, block 1209107 60-65).
 *
 * Retained only to label rows that already carry al_approle. The picklist is no longer
 * offered when assigning or when writing a permission rule — web roles are the vocabulary —
 * but the server still reads it, so an existing assignment keeps working and nothing is
 * silently revoked.
 */
export const LEGACY_APP_ROLE_VALUES: Record<string, number> = {
  'Tax Checker': 120910760,
  'AQS Checker': 120910761,
  Adviser: 120910762,
  'T&C Manager': 120910763,
  'Outcome Testing Manager': 120910764,
  Administrator: 120910765,
};

/** Reverse of LEGACY_APP_ROLE_VALUES: al_approle option value to role label. */
export const APP_ROLE_BY_VALUE: Record<number, string> = Object.fromEntries(
  Object.entries(LEGACY_APP_ROLE_VALUES).map(([role, value]) => [value, role]),
);

/**
 * Resource keys the permission model governs. Page keys gate navigation and
 * routes; capability keys gate a specific write action. Kept flat and stable so
 * a permission row references a string, never a screen internal.
 */
export const RESOURCE_KEYS = [
  // Navigable pages (09-Application-Screens).
  'page.dashboard',
  'page.cases',
  'page.imports',
  'page.reviews',
  'page.remediation',
  'page.reports',
  'page.exports',
  'page.admin.questions',
  'page.admin.advisers',
  'page.admin.templates',
  'page.admin.lists',
  'page.admin.security',
  'page.admin.users',
  // Capabilities (write actions gated independently of page view).
  'command.assign',
  'command.regrade',
  'command.signoff',
  'case.duedate',
  'remediation.complete',
  'question.retire',
  'export.generate',
  'permission.manage',
] as const;

export type ResourceKey = (typeof RESOURCE_KEYS)[number];

/** Access levels, low to high (AD-041, block 1209107 66-69). */
export const ACCESS_LEVELS = ['None', 'View', 'Edit', 'Manage'] as const;

export type AccessLevel = (typeof ACCESS_LEVELS)[number];

export const ACCESS_LEVEL_VALUES: Record<AccessLevel, number> = {
  None: 120910766,
  View: 120910767,
  Edit: 120910768,
  Manage: 120910769,
};

/** Reverse of ACCESS_LEVEL_VALUES: al_accesslevel option value to level label. */
export const ACCESS_LEVEL_BY_VALUE: Record<number, AccessLevel> = Object.fromEntries(
  (Object.entries(ACCESS_LEVEL_VALUES) as [AccessLevel, number][]).map(([level, value]) => [value, level]),
) as Record<number, AccessLevel>;

const ACCESS_RANK: Record<AccessLevel, number> = {
  None: 0,
  View: 1,
  Edit: 2,
  Manage: 3,
};

/** True when `have` meets or exceeds `need` on the access ladder. */
export function accessMeets(have: AccessLevel, need: AccessLevel): boolean {
  return ACCESS_RANK[have] >= ACCESS_RANK[need];
}

/**
 * One permission rule: a role has an access level on a resource. `role` is a built-in
 * AppRole label or a custom al_role code (AD-044), so custom roles enforce like built-ins.
 */
export interface PermissionRule {
  role: string;
  resource: ResourceKey;
  level: AccessLevel;
}

/**
 * The effective permission set resolved for the current user: the highest level
 * granted per resource across all of the user's roles.
 */
export type PermissionSet = Partial<Record<ResourceKey, AccessLevel>>;

/**
 * Seed matrix (AD-041): the rules an environment starts with, and the only rules the
 * client applies while al_pagepermission holds none (see `rulesInForce`). Once any rule
 * is stored the table alone decides, on both tiers. Levels trace to role ownership in
 * AD-020 (section ownership), AD-031 (outcome corrections) and AD-040 (allocation).
 */
export const DEFAULT_PERMISSIONS: readonly PermissionRule[] = [
  // Everyone who can sign in sees their own work.
  // Planners keep their emails and have no access to the system (project owner, 2026-09-23,
  // AD-218), so the role that still exists grants nothing - not even the dashboard.
  ...APP_ROLES.filter((role) => role !== 'AL Portal - Planner').map((role) => ({
    role,
    resource: 'page.dashboard' as ResourceKey,
    level: 'View' as AccessLevel,
  })),

  // Tax + AQS reviewers work cases and reviews (AD-020 section ownership).
  //
  // Edit rather than View on page.cases (project owner, 2026-09-12): the IO task extract
  // carries none of the case-header fields -- adviser code, paraplanner code, products,
  // case type, advice date, product/solution type, sample source, check date, vulnerable
  // client, tax check required, tax team disposition -- and the client's direction is that
  // Tax and AQS complete them by hand as part of doing the check. At View the panel renders
  // read-only and al_UpdateCaseDetails refuses the write, so the people named as filling
  // them in could not.
  //
  // This is the whole of page.cases, not just those fields: it also admits al_casestatus,
  // al_priority and al_taxcheckrequired, and that last one re-derives the route. NOT the
  // due date, since item 6 (2026-09-19) gave that its own key -- see `case.duedate` below.
  { role: 'AL Portal - Tax Reviewer', resource: 'page.cases', level: 'Edit' },
  { role: 'AL Portal - Tax Reviewer', resource: 'page.reviews', level: 'Edit' },
  { role: 'AL Portal - AQS Reviewer', resource: 'page.cases', level: 'Edit' },
  { role: 'AL Portal - AQS Reviewer', resource: 'page.reviews', level: 'Edit' },

  // Advisers own remediation (AD-020, BR-006).
  { role: 'AL Portal - Adviser Remediation', resource: 'page.remediation', level: 'Edit' },
  { role: 'AL Portal - Adviser Remediation', resource: 'remediation.complete', level: 'Edit' },

  // T&C Supervisor owns outcome corrections and sign-off (AD-031).
  { role: 'AL Portal - T&C Supervisor', resource: 'page.cases', level: 'Edit' },
  { role: 'AL Portal - T&C Supervisor', resource: 'page.remediation', level: 'Edit' },
  { role: 'AL Portal - T&C Supervisor', resource: 'page.reports', level: 'View' },
  { role: 'AL Portal - T&C Supervisor', resource: 'command.regrade', level: 'Edit' },
  { role: 'AL Portal - T&C Supervisor', resource: 'command.signoff', level: 'Edit' },

  // Outcome Testing Manager runs intake, allocation, reporting and exports (AD-040).
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.cases', level: 'Edit' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.imports', level: 'Edit' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.remediation', level: 'View' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.reports', level: 'View' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.exports', level: 'Manage' },
  // Fixes 5 (AD-162). The adviser -> T&C Manager mapping is run by the people who run
  // the checking process, not only by administrators: it decides who is told a sign-off
  // is waiting, and a stale mapping shows up as a manager who never hears.
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.admin.advisers', level: 'Manage' },
  // The letters are the checking team's words to advisers, so the people who run the
  // process own them - not only whoever administers the environment.
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.admin.templates', level: 'Manage' },
  // The case-header dropdowns are the checking team's own vocabulary - what a product type
  // or a sample source is called is their decision, not an administrator's - so the same
  // reasoning that gave them the letters gives them the lists (project owner, 2026-09-21).
  { role: 'AL Portal - Outcome Testing Manager', resource: 'page.admin.lists', level: 'Manage' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'command.assign', level: 'Edit' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'export.generate', level: 'Edit' },

  // AD-218: each team manager sees their team's cases and allocates their own discipline.
  // Their Dataverse security role is what scopes the rows. The team workload page this also
  // opened was withdrawn on 2026-09-25 (project owner: "it is not needed").
  ...(['AL Portal - Tax Team Manager', 'AL Portal - AQS Team Manager'] as const).flatMap((role) => [
    { role, resource: 'page.cases' as ResourceKey, level: 'Edit' as AccessLevel },
    { role, resource: 'command.assign' as ResourceKey, level: 'Edit' as AccessLevel },
  ]),

  // Moving a case's due date is a manager's act (project owner, 2026-09-19: "3 days, only
  // editable by managers in codeapps"). Its own key rather than a higher level on
  // page.cases, because page.cases Edit is what lets a Tax or AQS reviewer complete the
  // header fields the extract does not carry -- raising that bar would have taken the whole
  // header away from the people who are meant to fill it in.
  //
  // The two manager roles and the break-glass administrator, and nobody else. Portal
  // Administrator is deliberately absent: it holds page.cases at View, so it cannot reach
  // al_UpdateCaseDetails at all, and a grant that can never be exercised reads as an
  // authority somebody has.
  { role: 'AL Portal - T&C Supervisor', resource: 'case.duedate', level: 'Edit' },
  { role: 'AL Portal - Outcome Testing Manager', resource: 'case.duedate', level: 'Edit' },
  { role: 'Administrators', resource: 'case.duedate', level: 'Edit' },

  // Portal Administrator and Administrators both manage configuration and the permission
  // model. Two roles carry it because Administrators is the Power Pages built-in that real
  // administrators already hold, and dropping it would lock the current admins out.
  ...(['AL Portal - Portal Administrator', 'Administrators'] as const).flatMap((role) => [
    { role, resource: 'page.cases' as ResourceKey, level: 'View' as AccessLevel },
    { role, resource: 'page.imports' as ResourceKey, level: 'Edit' as AccessLevel },
    { role, resource: 'page.remediation' as ResourceKey, level: 'View' as AccessLevel },
    { role, resource: 'page.reports' as ResourceKey, level: 'View' as AccessLevel },
    { role, resource: 'page.exports' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.questions' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.advisers' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.templates' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.lists' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.security' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'page.admin.users' as ResourceKey, level: 'Manage' as AccessLevel },
    { role, resource: 'question.retire' as ResourceKey, level: 'Edit' as AccessLevel },
    { role, resource: 'export.generate' as ResourceKey, level: 'Edit' as AccessLevel },
    { role, resource: 'permission.manage' as ResourceKey, level: 'Manage' as AccessLevel },
  ]),
];

/** Collapse a set of rules for one or more roles into the highest level per resource. */
export function resolvePermissions(
  roles: readonly string[],
  rules: readonly PermissionRule[] = DEFAULT_PERMISSIONS,
): PermissionSet {
  const roleSet = new Set<string>(roles);
  const effective: PermissionSet = {};
  for (const rule of rules) {
    if (!roleSet.has(rule.role)) continue;
    const current = effective[rule.resource];
    if (!current || accessMeets(rule.level, current)) {
      effective[rule.resource] = rule.level;
    }
  }
  return effective;
}

/**
 * The rules the client resolves against, given the active rows al_pagepermission holds.
 *
 * The server gate (PermissionHelpers.MaxLevel) resolves from active al_pagepermission rows
 * and nothing else: a (role, resource) with no active rule is None. The client used to
 * overlay stored rules on DEFAULT_PERMISSIONS instead, so withdrawing a rule made the app
 * fall back to a coded default the server never had — the UI offered work the command then
 * refused as UNAUTHORIZED. Resolving from the stored rules alone, once any exist, keeps the
 * two tiers reading the same rules.
 *
 * DEFAULT_PERMISSIONS remains the seed and the bootstrap: an environment with no stored
 * rule at all resolves against it, so the first administrator can reach the security page
 * and seed the table. That is the one state in which the client is more permissive than
 * the server, and the server still gates every write.
 */
export function rulesInForce(stored: readonly PermissionRule[]): readonly PermissionRule[] {
  return stored.length > 0 ? stored : DEFAULT_PERMISSIONS;
}

/** The rules to gate the UI with, and whether they are the real ones. */
export interface RuleResolution {
  rules: readonly PermissionRule[];
  /** True when the rules could not be READ, so `rules` is a stand-in and not the truth. */
  unavailable: boolean;
}

/**
 * Separates the two states `rulesInForce` alone cannot tell apart: **"there are no stored
 * rules"** and **"the stored rules could not be read"** (AD-136).
 *
 * Both used to arrive as an empty array, and both fell back to DEFAULT_PERMISSIONS. The first
 * is correct — an unconfigured environment is meant to be seeded by the coded defaults. The
 * second is a lie: a user who lacks read privilege on al_pagepermission was gated by the
 * defaults rather than by the environment's configured rules, and got a menu that looked
 * entirely plausible and was wrong, with nothing anywhere saying so.
 *
 * Found on 2026-09-14: Zoe Ramwell held only `Basic User` in TEST and so saw different pages
 * from two System Administrators holding the same application role. The access problem was
 * real; what made it take an investigation rather than a glance was that the app reported
 * nothing unusual.
 *
 * The defaults are still what gates the UI in the failed case — there is nothing better to
 * gate it with, and every write is enforced server-side regardless — but the caller is told,
 * so it can say so.
 */
export function resolveRules(
  readSucceeded: boolean,
  stored: readonly PermissionRule[],
): RuleResolution {
  // A read that failed carries no authority, whatever it returned: a partial result is not a
  // rulebook, so `stored` is deliberately ignored here rather than merged.
  if (!readSucceeded) {
    return { rules: DEFAULT_PERMISSIONS, unavailable: true };
  }

  return { rules: rulesInForce(stored), unavailable: false };
}

/** The access level a permission set grants on a resource (None when absent). */
export function levelFor(set: PermissionSet, resource: ResourceKey): AccessLevel {
  return set[resource] ?? 'None';
}

/** True when the set grants at least `need` on `resource`. */
export function can(set: PermissionSet, resource: ResourceKey, need: AccessLevel = 'View'): boolean {
  return accessMeets(levelFor(set, resource), need);
}

/** Map a nav path to its governing page resource key, or null when ungated. */
export function pageResourceForPath(path: string): ResourceKey | null {
  if (path === '/') return 'page.dashboard';
  if (path.startsWith('/cases')) return 'page.cases';
  if (path.startsWith('/imports')) return 'page.imports';
  if (path.startsWith('/reviews')) return 'page.reviews';
  if (path.startsWith('/reports')) return 'page.reports';
  if (path.startsWith('/exports')) return 'page.exports';
  if (path.startsWith('/admin/questions')) return 'page.admin.questions';
  if (path.startsWith('/admin/advisers')) return 'page.admin.advisers';
  if (path.startsWith('/admin/templates')) return 'page.admin.templates';
  if (path.startsWith('/admin/lists')) return 'page.admin.lists';
  if (path.startsWith('/admin/security')) return 'page.admin.security';
  // The registry (Task 9, 2026-09-22): its Role column writes al_userrolemapping, an
  // authorisation source, so it is gated as administration rather than page.cases.
  if (path.startsWith('/admin/people')) return 'page.admin.users';
  return null;
}
