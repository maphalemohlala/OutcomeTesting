import type { ReactNode } from 'react';
import { usePermissions } from './permissionContext';
import { PageUnavailable } from '../../components/feedback/PageUnavailable';
import type { AccessLevel, ResourceKey } from '../../types/permissions';

/**
 * Gates its children behind a resource/level (AD-041). The client gate is
 * advisory: it hides UI the user cannot use, but the authoritative check runs
 * server-side. `fallback` is shown when the user is short of the required level;
 * a route-level gate defaults to a "no access" screen.
 *
 * Until the effective set is resolved the gate renders `pending` rather than deciding, so a
 * half-loaded answer never opens anything.
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
  const { accessUnknown } = usePermissions();

  // When the app could not establish this person's roles at all, the shell already carries
  // the banner that says so, and that is the whole message (project owner, 2026-09-21).
  // Adding this screen's own panel underneath would say "your role does not grant access to
  // this screen", which asserts they HAVE roles and that these particular ones fall short -
  // neither of which is known. Two answers, one of them invented, is worse than one.
  if (accessUnknown) {
    return null;
  }

  return (
    <PermissionGate
      resource={resource}
      need={need}
      pending={<p role="status">Checking your access…</p>}
      fallback={
        <PageUnavailable
          title="No access"
          purpose="Your role does not grant access to this screen. Ask an administrator to assign it in Security configuration."
          heading="No access"
          detail="This screen exists and is working. Your application role simply does not include it, so there is nothing here for you to see. An administrator can grant it in Security configuration."
          blockedBy={[`Permission: ${resource} (${need})`]}
        />
      }
    >
      {children}
    </PermissionGate>
  );
}
