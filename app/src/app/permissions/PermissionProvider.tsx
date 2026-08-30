import { useEffect, useMemo, useState } from 'react';
import { odataEscape } from '../../services/odata';
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
import {
  Al_pagepermissionsService,
  Al_userrolemappingsService,
  Al_usersService,
} from '../../generated';
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
  const [mapResult, permResult, userResult] = await Promise.all([
    Al_userrolemappingsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
    Al_pagepermissionsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
    Al_usersService.getAll({ filter: `al_workemail eq '${odataEscape(email.trim().toLowerCase())}'`, top: 1 }),
  ]);

  const mappings = mapResult.success ? mapResult.data : null;
  const perms = permResult.success ? permResult.data : [];

  // Deactivation (OD-010) withdraws access, so a deactivated registry row resolves to no
  // roles here as well as server-side. Mirrored rather than relied on: the server is the
  // gate, and this only stops the UI offering a leaver work it would then refuse.
  const registered = userResult.success ? userResult.data : [];
  const deactivated = registered.length > 0 && registered[0].al_isactive === false;
  if (deactivated) {
    return { roles: [], permissions: resolvePermissions([]) };
  }

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
    // While resolving, the permissive set stands in so nothing computes against an empty
    // one — but `ready` is false, and the gates render their pending state rather than
    // this set, so it is never what decides whether a screen is shown.
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
