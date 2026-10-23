import { useEffect, useMemo, useState } from 'react';
import { can as canAccess, levelFor, resolvePermissions } from '../../types/permissions';
import { useCurrentUser } from '../../services/auth/useCurrentUser';
import { PermissionContext, type PermissionContextValue } from './permissionContext';
import { loadPermissions, type Resolved } from './loadPermissions';

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
