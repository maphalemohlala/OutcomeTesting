import { useEffect, useMemo, useState } from 'react';
import {
  APP_ROLES,
  APP_ROLE_BY_VALUE,
  ACCESS_LEVEL_BY_VALUE,
  can as canAccess,
  DEFAULT_PERMISSIONS,
  levelFor,
  overlayRules,
  resolvePermissions,
  type PermissionRule,
  type PermissionSet,
  type ResourceKey,
} from '../../types/permissions';
import { Al_pagepermissionsService, Al_userrolemappingsService } from '../../generated';
import { useCurrentUser } from '../../services/auth/useCurrentUser';
import { PermissionContext, type PermissionContextValue } from './permissionContext';

interface Resolved {
  roles: string[];
  permissions: PermissionSet;
}

/**
 * Resolves the effective permissions for the signed-in user (AD-041). Roles come
 * from al_userrolemapping keyed on work email (AD-010); admin-maintained rules in
 * al_pagepermission overlay the code defaults. Fail-open for view (permissive when
 * the mapping table is empty or unreadable) is safe because every write is enforced
 * server-side by the Custom API commands.
 */
async function loadPermissions(email: string): Promise<Resolved> {
  const [mapResult, permResult] = await Promise.all([
    Al_userrolemappingsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
    Al_pagepermissionsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
  ]);

  const mappings = mapResult.success ? mapResult.data : null;
  const perms = permResult.success ? permResult.data : [];

  // Bootstrap / fail-open: before any mapping exists (or if the table is unreadable),
  // grant every role so the first administrator can configure access. Server commands
  // still gate writes, so this only affects what the UI shows.
  let roles: string[];
  if (!mappings || mappings.length === 0) {
    roles = [...APP_ROLES];
  } else {
    const needle = email.trim().toLowerCase();
    roles = mappings
      .filter((m) => (m.al_useremail ?? '').trim().toLowerCase() === needle)
      .map((m) => m.al_rolecode?.trim() || APP_ROLE_BY_VALUE[Number(m.al_approle)])
      .filter((role): role is string => Boolean(role));
  }

  const dataRules: PermissionRule[] = perms
    .map((p): PermissionRule | null => {
      const role = p.al_rolecode?.trim() || APP_ROLE_BY_VALUE[Number(p.al_approle)];
      const level = ACCESS_LEVEL_BY_VALUE[Number(p.al_accesslevel)];
      if (!role || !level || !p.al_resourcekey) return null;
      return { role, resource: p.al_resourcekey as ResourceKey, level };
    })
    .filter((rule): rule is PermissionRule => Boolean(rule));

  const rules = overlayRules(DEFAULT_PERMISSIONS, dataRules);
  return { roles, permissions: resolvePermissions(roles, rules) };
}

export function PermissionProvider({ children }: { children: React.ReactNode }) {
  const userState = useCurrentUser();
  const email = userState.status === 'ready' ? userState.user.userPrincipalName ?? '' : '';
  const [resolved, setResolved] = useState<Resolved | null>(null);

  useEffect(() => {
    if (userState.status === 'loading') return;
    let cancelled = false;
    loadPermissions(email)
      .then((result) => {
        if (!cancelled) setResolved(result);
      })
      .catch(() => {
        // On an unexpected failure, stay permissive for view; server still gates writes.
        if (!cancelled) setResolved({ roles: [...APP_ROLES], permissions: resolvePermissions(APP_ROLES) });
      });
    return () => {
      cancelled = true;
    };
  }, [email, userState.status]);

  const value = useMemo<PermissionContextValue>(() => {
    // While resolving, stay permissive so no screen flashes locked; not yet authoritative.
    const roles = resolved ? resolved.roles : [...APP_ROLES];
    const permissions = resolved ? resolved.permissions : resolvePermissions(APP_ROLES);
    return {
      ready: resolved !== null,
      roles,
      permissions,
      can: (resource, need) => canAccess(permissions, resource, need),
      level: (resource) => levelFor(permissions, resource),
    };
  }, [resolved]);

  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
}
