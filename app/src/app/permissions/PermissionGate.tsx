import type { ReactNode } from 'react';
import { usePermissions } from './permissionContext';
import { NotBuiltYet } from '../../components/feedback/NotBuiltYet';
import type { AccessLevel, ResourceKey } from '../../types/permissions';

/**
 * Gates its children behind a resource/level (AD-041). The client gate is
 * advisory: it hides UI the user cannot use, but the authoritative check runs
 * server-side. `fallback` is shown when the user is short of the required level;
 * a route-level gate defaults to a "no access" screen.
 *
 * Until the effective set is resolved the gate renders `pending` rather than deciding.
 * The provider stands a permissive set in while it loads, so answering `can` during that
 * window would open every gate for as long as the read takes — and indefinitely if it
 * never returns. Waiting is what keeps the permissive set out of the decision.
 */
export function PermissionGate({
  resource,
  need = 'View',
  children,
  fallback = null,
  pending = null,
}: {
  resource: ResourceKey;
  need?: AccessLevel;
  children: ReactNode;
  fallback?: ReactNode;
  pending?: ReactNode;
}) {
  const { can, ready } = usePermissions();
  if (!ready) return <>{pending}</>;
  return can(resource, need) ? <>{children}</> : <>{fallback}</>;
}

/** Route-level gate: renders a no-access screen instead of the page. */
export function RequirePermission({
  resource,
  need = 'View',
  children,
}: {
  resource: ResourceKey;
  need?: AccessLevel;
  children: ReactNode;
}) {
  return (
    <PermissionGate
      resource={resource}
      need={need}
      pending={<p role="status">Checking your access…</p>}
      fallback={
        <NotBuiltYet
          title="No access"
          purpose="Your role does not grant access to this screen. Ask an administrator to assign it in Security configuration."
          blockedBy={[`Permission: ${resource} (${need})`]}
        />
      }
    >
      {children}
    </PermissionGate>
  );
}
