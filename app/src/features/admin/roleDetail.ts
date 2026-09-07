import {
  ACCESS_LEVELS,
  DEFAULT_PERMISSIONS,
  levelFor,
  overlayRules,
  RESOURCE_KEYS,
  resolvePermissions,
  type AccessLevel,
  type PermissionRule,
  type ResourceKey,
} from '../../types/permissions';
import type { PagePermissionRow, RoleMappingRow } from './useSecurityConfig';

/**
 * Where a role's detail screen lives. The code is the business key assignments and
 * permission rules match on (AD-044), so it is what the address carries — a role name with
 * spaces round-trips through encoding, as the person routes already do.
 */
export function roleDetailPath(roleCode: string): string {
  return `/admin/security/roles/${encodeURIComponent(roleCode)}`;
}

/** Whether a level came from the code defaults or from a rule an administrator stored. */
export type GrantSource = 'Default' | 'Set by administrator';

export interface RoleGrant {
  resource: ResourceKey;
  level: AccessLevel;
  source: GrantSource;
  /** The stored rule this row resolves to, so the row can act on it. Null for a default. */
  overrideId: string | null;
}

export interface RoleHolder {
  id: string;
  email: string;
  /** False once the assignment is withdrawn; the row is kept for the audit trail. */
  active: boolean;
}

/**
 * Whether two role codes name the same role. Compared loosely because the code is a
 * free-text column (AD-044) written by three different paths — the app, the portal and the
 * seed matrix — and a difference of casing or trailing space is not a different role.
 */
export function sameRoleCode(a: string, b: string): boolean {
  return a.trim().toLowerCase() === b.trim().toLowerCase();
}

function isAccessLevel(value: string): value is AccessLevel {
  return (ACCESS_LEVELS as readonly string[]).includes(value);
}

function isResourceKey(value: string): value is ResourceKey {
  return (RESOURCE_KEYS as readonly string[]).includes(value);
}

/**
 * What one role grants, resolved exactly the way the app enforces it.
 *
 * This runs the same `overlayRules` → `resolvePermissions` pair that PermissionProvider
 * uses at sign-in, against this one role, so the screen cannot drift from what a person
 * holding the role would actually get. Writing a second resolution here would be the one
 * way to make this page confidently wrong.
 *
 * Withdrawn rules are left out for the same reason the provider filters on `statecode eq 0`:
 * they are kept for the audit trail but no longer apply, so the resource falls back to its
 * default. A row that no map recognises is dropped rather than shown as a grant — the
 * security page already surfaces those as unrecognised, and guessing at one here would put
 * an access level on screen that nothing enforces.
 */
export function buildRoleGrants(
  roleCode: string,
  permissions: readonly PagePermissionRow[],
): RoleGrant[] {
  const overrideById = new Map<ResourceKey, string>();
  const overrides: PermissionRule[] = [];

  for (const row of permissions) {
    if (!row.active || !sameRoleCode(row.role, roleCode)) continue;
    if (!isResourceKey(row.resource) || !isAccessLevel(row.level)) continue;
    overrides.push({ role: roleCode, resource: row.resource, level: row.level });
    overrideById.set(row.resource, row.id);
  }

  // resolvePermissions matches the role name exactly, so the defaults are re-keyed to the
  // code as written. That matters for a role whose web role name differs from the default
  // matrix only in casing: it should still show the defaults it will be granted.
  const defaults = DEFAULT_PERMISSIONS.filter((rule) => sameRoleCode(rule.role, roleCode)).map(
    (rule) => ({ ...rule, role: roleCode }),
  );
  const set = resolvePermissions([roleCode], overlayRules(defaults, overrides));

  return RESOURCE_KEYS.map((resource) => {
    const overrideId = overrideById.get(resource) ?? null;
    return {
      resource,
      level: levelFor(set, resource),
      source: overrideId ? ('Set by administrator' as const) : ('Default' as const),
      overrideId,
    };
  }).filter((grant) => grant.level !== 'None' || grant.overrideId !== null);
}

/**
 * The people holding a role, active assignments first and withdrawn ones after.
 *
 * Read from the role mappings, as the People page is (AD-041): assigning writes both the
 * mapping and the web role association, so the mapping carries the same facts and is a
 * table the app can already read. An assignment made in Power Pages management writes no
 * mapping row and so does not appear here — the screen says as much rather than implying
 * the list is complete.
 */
export function buildRoleHolders(
  roleCode: string,
  mappings: readonly RoleMappingRow[],
): RoleHolder[] {
  return mappings
    .filter((mapping) => sameRoleCode(mapping.role, roleCode))
    .map((mapping) => ({
      id: mapping.id,
      email: mapping.email.trim(),
      active: mapping.active,
    }))
    .sort(
      (a, b) =>
        Number(b.active) - Number(a.active) ||
        a.email.localeCompare(b.email),
    );
}

/** One person's relationship to one role, as al_GetRoleHolders reports it. */
export interface RoleHolderRecord {
  email: string;
  name: string | null;
  /** Null when no mapping row exists — the role was granted in Power Pages only. */
  mappingId: string | null;
  mappingActive: boolean | null;
  associated: boolean;
}

export type HolderState =
  | 'consistent'
  | 'portal-only'
  | 'withdrawn-still-granted'
  | 'association-missing';

export interface HolderClassification {
  state: HolderState;
  label: string;
  canAdopt: boolean;
  canRevoke: boolean;
}

/**
 * What the two sources say, and what an administrator can do about it (AD-089).
 *
 * Adopt converges on granted and Revoke converges on not granted, so a state offers the
 * action that would make the two sources agree — and a state where they already agree
 * offers neither.
 *
 * A withdrawn mapping with no association is `consistent`: both sources say the person
 * does not hold the role, which is exactly what a completed withdrawal looks like.
 */
export function classifyHolder(record: RoleHolderRecord): HolderClassification {
  const held = record.mappingActive === true;

  if (record.mappingId === null && record.associated) {
    return {
      state: 'portal-only',
      label: 'Granted in Power Pages, not adopted',
      canAdopt: true,
      canRevoke: true,
    };
  }

  if (record.mappingActive === false && record.associated) {
    return {
      state: 'withdrawn-still-granted',
      label: 'Withdrawn in app, still granted',
      canAdopt: true,
      canRevoke: true,
    };
  }

  if (held && !record.associated) {
    return {
      state: 'association-missing',
      label: 'Assigned in app, association missing',
      canAdopt: true,
      canRevoke: true,
    };
  }

  return {
    state: 'consistent',
    label: held ? 'Assigned in app' : 'Withdrawn',
    canAdopt: false,
    canRevoke: false,
  };
}
