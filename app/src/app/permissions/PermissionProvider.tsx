import { useEffect, useMemo, useState } from 'react';
import { odataEscape } from '../../services/odata';
import {
  APP_ROLE_BY_VALUE,
  ACCESS_LEVEL_BY_VALUE,
  can as canAccess,
  levelFor,
  resolvePermissions,
  resolveRules,
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
  /**
   * True when the RULES could not be read, so `permissions` rests on the coded defaults
   * rather than the environment's stored rulebook (AD-136). The caller's roles are known.
   */
  rulesUnavailable: boolean;
  /** True when the caller's own roles could not be established at all, so nothing is granted. */
  accessUnknown: boolean;
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
export async function loadPermissions(email: string): Promise<Resolved> {
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
    return {
      roles: [],
      permissions: resolvePermissions([]),
      rulesUnavailable: false,
      accessUnknown: false,
    };
  }

  // Bootstrap / fail-open: the trigger is no longer "the mapping table is unreadable or has
  // zero rows" (the client no longer reads that table at all) — it is "al_GetMyRoles could
  // not answer", whether the call itself failed or its response will not parse. Either way
  // the permissive set stands in so the first administrator can still configure access, and
  // server commands still gate every write, so this only affects what the UI offers. A call
  // that SUCCEEDS and answers an empty array is not the bootstrap case: al_GetMyRoles unions
  // al_userrolemapping with the caller's web role associations and applies the AD-090
  // exclusion server-side, so an empty result means the caller genuinely holds nothing.
  // If we cannot establish what this person holds, they get NOTHING, and a banner saying so
  // (project owner, 2026-09-21).
  //
  // This used to hand back every role in the product. The reason given was bootstrap - so a
  // first administrator could still configure a fresh environment - and that reason does not
  // survive contact with the code. A fresh environment does not FAIL this call, it ANSWERS
  // it: al_GetMyRoles returns an empty array, which is a real answer and already resolves to
  // no access. GetMyRolesPlugin has no administrator short-circuit either. So the permissive
  // set never served the case it was justified by; it only ever fired when the call ERRORED,
  // which is precisely when the app knows least about who is asking.
  //
  // What it did instead was F44, live in DEV on 2026-09-21: an account holding Outcome
  // Testing App User but not Basic User faulted this call, was handed every role, and was
  // shown a ten-item administrative menu including Case intake and Security configuration,
  // both of which opened with real data in them. Removing a privilege made the app offer
  // MORE. Writes were refused throughout - the server gate never wavered - but reads on
  // those screens are gated here and nowhere else, so this was the only thing standing
  // between a mis-provisioned account and the whole application.
  //
  // An empty answer is still an answer and is NOT this case: it means they genuinely hold
  // nothing, which the menu already reflects without any banner.
  let roles: string[] = [];
  let accessUnknown = false;
  if (!rolesResult.ok) {
    accessUnknown = true;
  } else {
    try {
      roles = JSON.parse(rolesResult.data.RoleCodes) as string[];
    } catch {
      accessUnknown = true;
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

  // `permResult.success` is the distinction that matters: a read that FAILED and a read that
  // legitimately found no rules both arrive as an empty array, and treating them alike is what
  // gated a mis-provisioned user by the coded defaults while telling nobody (AD-136).
  const { rules, unavailable } = resolveRules(permResult.success, dataRules);

  // With no roles established there is nothing to resolve rules against, and no point
  // telling somebody the RULEBOOK was a stand-in when the more basic answer is that we do
  // not know who they are.
  if (accessUnknown) {
    return {
      roles: [],
      permissions: resolvePermissions([]),
      rulesUnavailable: false,
      accessUnknown: true,
    };
  }

  return {
    roles,
    permissions: resolvePermissions(roles, rules),
    rulesUnavailable: unavailable,
    accessUnknown: false,
  };
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
        // Same rule as a failed roles read, for the same reason: an unexpected failure is
        // the moment the app knows LEAST about who is asking, so it is the worst possible
        // moment to assume the answer is yes.
        if (!cancelled) {
          setResolved({
            roles: [],
            permissions: resolvePermissions([]),
            rulesUnavailable: false,
            accessUnknown: true,
          });
        }
      });
    return () => {
      cancelled = true;
    };
  }, [email, userState.status]);

  const value = useMemo<PermissionContextValue>(() => {
    // While resolving, nothing is granted. `ready` is false and the gates render their
    // pending state, so this set does not decide whether a SCREEN is shown - but the
    // navigation rail calls `can` directly, and a permissive set here put the full
    // administrative menu on screen for the moment before the answer arrived.
    const roles = resolved ? resolved.roles : [];
    const permissions = resolved ? resolved.permissions : resolvePermissions([]);
    return {
      ready: resolved !== null,
      roles,
      permissions,
      // False while still resolving: nothing is known to be wrong yet, and a banner that
      // flashes on every load would be noise the reader learns to ignore.
      rulesUnavailable: resolved ? resolved.rulesUnavailable : false,
      accessUnknown: resolved ? resolved.accessUnknown : false,
      can: (resource, need) => canAccess(permissions, resource, need),
      level: (resource) => levelFor(permissions, resource),
    };
  }, [resolved]);

  return <PermissionContext.Provider value={value}>{children}</PermissionContext.Provider>;
}
