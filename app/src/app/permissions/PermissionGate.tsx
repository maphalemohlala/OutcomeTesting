import type { ReactNode } from 'react';
import { usePermissions } from './permissionContext';
import { NotBuiltYet } from '../../components/feedback/NotBuiltYet';
import type { AccessLevel, ResourceKey } from '../../types/permissions';

/**
 * Gates its children behind a resource/level (AD-041). The client gate is
 * advisory: it hides UI the user cannot use, but the authoritative check runs
 * server-side. `fallback` is shown when the user is short of the required level;
 * a route-level gate defaults to a "no access" screen.
 */
export function PermissionGate({
  resource,
  need = 'View',
  children,
  fallback = null,
}: {
  resource: ResourceKey;
  need?: AccessLevel;
  children: ReactNode;
  fallback?: ReactNode;
}) {
  const { can } = usePermissions();
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
