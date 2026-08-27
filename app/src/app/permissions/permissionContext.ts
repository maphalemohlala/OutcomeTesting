import { createContext, useContext } from 'react';
import type { AccessLevel, PermissionSet, ResourceKey } from '../../types/permissions';

export interface PermissionContextValue {
  /** True once the effective set is resolved for the signed-in user. */
  ready: boolean;
  roles: readonly string[];
  permissions: PermissionSet;
  can: (resource: ResourceKey, need?: AccessLevel) => boolean;
  level: (resource: ResourceKey) => AccessLevel;
}

export const PermissionContext = createContext<PermissionContextValue | null>(null);

export function usePermissions(): PermissionContextValue {
  const value = useContext(PermissionContext);
  if (!value) {
    throw new Error('usePermissions must be used within a PermissionProvider.');
  }
  return value;
}
