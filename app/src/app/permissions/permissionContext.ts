import { createContext, useContext } from 'react';
import type { AccessLevel, PermissionSet, ResourceKey } from '../../types/permissions';

export interface PermissionContextValue {
  /** True once the effective set is resolved for the signed-in user. */
  ready: boolean;
  roles: readonly string[];
  permissions: PermissionSet;
  /**
   * True when al_pagepermission could not be READ, so `permissions` is the coded defaults
   * standing in for rules nobody has seen — not the environment's configured rules (AD-136).
   * An unconfigured environment with no rules stored is NOT this: that is a real answer.
   */
  rulesUnavailable: boolean;
  /**
   * True when the caller's OWN ROLES could not be established — `al_GetMyRoles` failed or
   * answered something unparseable — so nothing is granted and every screen refuses.
   *
   * Distinct from holding no roles, which is a real answer arrived at honestly, and from
   * `rulesUnavailable`, where the roles ARE known and only the rulebook is a stand-in. Here
   * the app knows nothing about this person and says so rather than guessing (project owner,
   * 2026-09-21).
   */
  accessUnknown: boolean;
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
