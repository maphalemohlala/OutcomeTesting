/*
 * Application RBAC model (AD-041). This is the single client-side source of truth
 * for what a role may see and do; the server-side Custom API commands (AD-003)
 * enforce the same rules against Dataverse privileges, so the app layer is
 * advisory only and never the security boundary (AGENTS.md invariant).
 *
 * Roles trace to AD-020/AD-031. Resource keys and access levels are the AD-041
 * vocabulary. Do not add a role or capability here without a requirement/AD ID.
 */

/** App roles, reused from AD-020/AD-031 rather than invented (rule 4). */
export const APP_ROLES = [
  'Tax Checker',
  'AQS Checker',
  'Adviser',
  'T&C Manager',
  'Outcome Testing Manager',
  'Administrator',
] as const;

export type AppRole = (typeof APP_ROLES)[number];

/** Dataverse option values for al_approle (AD-041, block 1209107 60-65). */
export const APP_ROLE_VALUES: Record<AppRole, number> = {
  'Tax Checker': 120910760,
  'AQS Checker': 120910761,
  Adviser: 120910762,
  'T&C Manager': 120910763,
  'Outcome Testing Manager': 120910764,
  Administrator: 120910765,
};

/** Reverse of APP_ROLE_VALUES: al_approle option value to role label. */
export const APP_ROLE_BY_VALUE: Record<number, AppRole> = Object.fromEntries(
  (Object.entries(APP_ROLE_VALUES) as [AppRole, number][]).map(([role, value]) => [value, role]),
) as Record<number, AppRole>;

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
  'page.admin.security',
  'page.admin.users',
  // Capabilities (write actions gated independently of page view).
  'command.assign',
  'command.regrade',
  'command.signoff',
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
 * Seed matrix (AD-041). This is the default an Administrator can then edit in the
 * app via al_PagePermission; it also lets the client degrade safely to a sane
 * baseline before the Dataverse rows load. Levels trace to role ownership in
 * AD-020 (section ownership), AD-031 (outcome corrections) and AD-040 (allocation).
 */
export const DEFAULT_PERMISSIONS: readonly PermissionRule[] = [
  // Everyone who can sign in sees their own work.
  ...APP_ROLES.map((role) => ({ role, resource: 'page.dashboard' as ResourceKey, level: 'View' as AccessLevel })),

  // Tax + AQS checkers work cases and reviews.
  { role: 'Tax Checker', resource: 'page.cases', level: 'View' },
  { role: 'Tax Checker', resource: 'page.reviews', level: 'Edit' },
  { role: 'AQS Checker', resource: 'page.cases', level: 'View' },
  { role: 'AQS Checker', resource: 'page.reviews', level: 'Edit' },

  // Advisers own remediation (AD-020, BR-006).
  { role: 'Adviser', resource: 'page.remediation', level: 'Edit' },
  { role: 'Adviser', resource: 'remediation.complete', level: 'Edit' },

  // T&C Manager owns outcome corrections and sign-off (AD-031).
  { role: 'T&C Manager', resource: 'page.cases', level: 'Edit' },
  { role: 'T&C Manager', resource: 'page.remediation', level: 'Edit' },
  { role: 'T&C Manager', resource: 'page.reports', level: 'View' },
  { role: 'T&C Manager', resource: 'command.regrade', level: 'Edit' },
  { role: 'T&C Manager', resource: 'command.signoff', level: 'Edit' },
  { role: 'T&C Manager', resource: 'command.assign', level: 'Edit' },

  // Outcome Testing Manager runs intake, allocation, reporting and exports.
  { role: 'Outcome Testing Manager', resource: 'page.cases', level: 'Edit' },
  { role: 'Outcome Testing Manager', resource: 'page.imports', level: 'Edit' },
  { role: 'Outcome Testing Manager', resource: 'page.reports', level: 'View' },
  { role: 'Outcome Testing Manager', resource: 'page.exports', level: 'Manage' },
  { role: 'Outcome Testing Manager', resource: 'command.assign', level: 'Edit' },
  { role: 'Outcome Testing Manager', resource: 'export.generate', level: 'Edit' },

  // Administrator manages configuration and the permission model itself.
  { role: 'Administrator', resource: 'page.cases', level: 'View' },
  { role: 'Administrator', resource: 'page.imports', level: 'Edit' },
  { role: 'Administrator', resource: 'page.reports', level: 'View' },
  { role: 'Administrator', resource: 'page.exports', level: 'Manage' },
  { role: 'Administrator', resource: 'page.admin.questions', level: 'Manage' },
  { role: 'Administrator', resource: 'page.admin.security', level: 'Manage' },
  { role: 'Administrator', resource: 'page.admin.users', level: 'Manage' },
  { role: 'Administrator', resource: 'question.retire', level: 'Edit' },
  { role: 'Administrator', resource: 'export.generate', level: 'Edit' },
  { role: 'Administrator', resource: 'permission.manage', level: 'Manage' },
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
 * Overlays admin-maintained rules on the code defaults (AD-041). A stored rule
 * replaces the default for its exact (role, resource), so an Administrator can
 * both grant and revoke without the al_pagepermission table having to be fully
 * seeded. This keeps the app safe before any rule is stored.
 */
export function overlayRules(
  base: readonly PermissionRule[],
  overrides: readonly PermissionRule[],
): PermissionRule[] {
  const key = (rule: PermissionRule) => `${rule.role}|${rule.resource}`;
  const merged = new Map<string, PermissionRule>(base.map((rule) => [key(rule), rule]));
  for (const override of overrides) {
    merged.set(key(override), override);
  }
  return [...merged.values()];
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
  if (path.startsWith('/admin/security')) return 'page.admin.security';
  return null;
}
