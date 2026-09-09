import { useEffect, useMemo, useState } from 'react';
import { odataEscape } from '../../services/odata';
import {
  APP_ROLES,
  APP_ROLE_BY_VALUE,
  ACCESS_LEVEL_BY_VALUE,
  can as canAccess,
  levelFor,
  resolvePermissions,
  rulesInForce,
  type PermissionRule,
  type PermissionSet,
  type ResourceKey,
} from '../../types/permissions';
import {
  Al_pagepermissionsService,
  ContactsService,
} from '../../generated';
import { executeCommand } from '../../services/commands/commandClient';
import { useCurrentUser } from '../../services/auth/useCurrentUser';
import { PermissionContext, type PermissionContextValue } from './permissionContext';

interface Resolved {
  roles: string[];
  permissions: PermissionSet;
}

/**
 * Resolves the effective permissions for the signed-in user (AD-041). Roles come from
 * `al_GetMyRoles` (AD-089, AD-090), which asks the server what the caller holds rather than
 * having the client re-derive it. Rules come from al_pagepermission alone once any are
 * stored, exactly as the server gate reads them; the code defaults apply only to an
 * environment with no stored rule (`rulesInForce`). Fail-open for view (permissive when the
 * API call fails outright) is safe because every write is enforced server-side by the
 * Custom API commands.
 */
async function loadPermissions(email: string): Promise<Resolved> {
  const [rolesResult, permResult, userResult] = await Promise.all([
    executeCommand<{ RoleCodes: string }>('al_GetMyRoles', {}),
    Al_pagepermissionsService.getAll({ filter: 'statecode eq 0', top: 5000 }),
    ContactsService.getAll({
      filter: `emailaddress1 eq '${odataEscape(email.trim().toLowerCase())}'`,
      top: 1,
    }),
  ]);

  const perms = permResult.success ? permResult.data : [];

  // Deactivation (OD-010) withdraws access, so a deactivated registry row resolves to no
  // roles here as well as server-side. Mirrored rather than relied on: the server is the
  // gate, and this only stops the UI offering a leaver work it would then refuse.
  const registered = userResult.success ? userResult.data : [];
  const deactivated = registered.length > 0 && Number(registered[0].statecode) !== 0;
  if (deactivated) {
    return { roles: [], permissions: resolvePermissions([]) };
  }

  // Bootstrap / fail-open: the trigger is no longer "the mapping table is unreadable or has
  // zero rows" (the client no longer reads that table at all) — it is "al_GetMyRoles could
  // not answer", whether the call itself failed or its response will not parse. Either way
  // the permissive set stands in so the first administrator can still configure access, and
  // server commands still gate every write, so this only affects what the UI offers. A call
  // that SUCCEEDS and answers an empty array is not the bootstrap case: al_GetMyRoles unions
  // al_userrolemapping with the caller's web role associations and applies the AD-090
  // exclusion server-side, so an empty result means the caller genuinely holds nothing.
  let roles: string[];
  if (!rolesResult.ok) {
    roles = [...APP_ROLES];
  } else {
    try {
      roles = JSON.parse(rolesResult.data.RoleCodes) as string[];
    } catch {
      roles = [...APP_ROLES];
    }
  }

  const dataRules: PermissionRule[] = perms
    .map((p): PermissionRule | null => {
      const role = p.al_rolecode?.trim() || APP_ROLE_BY_VALUE[Number(p.al_approle)];
      const level = ACCESS_LEVEL_BY_VALUE[Number(p.al_accesslevel)];
      if (!role || !level || !p.al_resourcekey) return null;
      return { role, resource: p.al_resourcekey as ResourceKey, level };
    })
    .filter((rule): rule is PermissionRule => Boolean(rule));

  return { roles, permissions: resolvePermissions(roles, rulesInForce(dataRules)) };
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
