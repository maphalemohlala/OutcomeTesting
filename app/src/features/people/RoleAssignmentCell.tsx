import { useState } from 'react';
import { canWithdraw, mappingFor } from './roleAssignment';
import type { RoleMappingRow } from '../admin/useSecurityConfig';

interface Props {
  email: string;
  roles: string[];
  mappings: readonly RoleMappingRow[];
  /**
   * The roles this page may grant: the live web role names from `useRoles`.
   *
   * Passed in rather than imported, because the assignable set is server data. A hard-coded
   * list here sent the retired `al_Role` codes and every Grant was refused with "The role
   * code does not match an active role" - see peopleFilters.
   */
  grantable: readonly string[];
  canManage: boolean;
  busy: boolean;
  onGrant: (email: string, role: string) => void;
  onWithdraw: (mappingId: string, email: string, role: string) => void;
}

/**
 * One person's roles, and an administrator's controls to change them.
 *
 * Every role they hold is listed, not just one (D8): a T&C Manager who also advises is one
 * person holding two, and picking one to display would misreport them. That includes the
 * application-access roles - Administrator, Outcome Testing Manager, Reviewer, Read Only
 * User - which this page does not grant and therefore does not withdraw either: a one-click
 * Withdraw beside a role the Grant dropdown cannot offer back is a one-way door, and the
 * cell says where those are managed instead (2026-09-22 review).
 *
 * The control says "grants access" in as many words. This writes al_userrolemapping, which
 * PermissionHelpers reads when it decides what a caller may do — an administrator tagging
 * somebody "Adviser" for reporting is also letting them in, and that should not be a
 * surprise discovered later.
 */
export function RoleAssignment({
  email,
  roles,
  mappings,
  grantable,
  canManage,
  busy,
  onGrant,
  onWithdraw,
}: Props) {
  const [choice, setChoice] = useState('');

  if (email === '') {
    // Someone named on a case who is not in the registry. There is no mapping to hold,
    // because al_userrolemapping is keyed on work email.
    return <span className="people__muted">Not in directory</span>;
  }

  return (
    <div className="people__roles">
      {roles.length === 0 ? (
        <span className="people__muted">No roles</span>
      ) : (
        <ul className="people__role-list">
          {roles.map((role) => {
            const held = mappingFor(mappings, email, role);
            return (
              <li key={role}>
                <span>{role}</span>
                {canManage && held && canWithdraw(role, grantable) ? (
                  <button
                    type="button"
                    className="people__role-withdraw"
                    disabled={busy}
                    onClick={() => onWithdraw(held.id, email, role)}
                  >
                    Withdraw
                  </button>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}

      {canManage && roles.some((role) => !canWithdraw(role, grantable)) ? (
        <p className="people__muted people__role-note">
          Roles this page cannot grant back are shown here but managed on Security
          configuration.
        </p>
      ) : null}

      {canManage ? (
        <div className="people__role-grant">
          <label className="visually-hidden" htmlFor={`grant-${email}`}>
            Grant a role to {email}
          </label>
          <select
            id={`grant-${email}`}
            value={choice}
            disabled={busy}
            onChange={(event) => setChoice(event.target.value)}
          >
            <option value="">Grant a role&hellip;</option>
            {grantable
              .filter((role) => !mappingFor(mappings, email, role))
              .map((role) => (
                <option key={role} value={role}>
                  {role}
                </option>
              ))}
          </select>
          <button
            type="button"
            disabled={busy || choice === '' || !grantable.includes(choice)}
            onClick={() => {
              onGrant(email, choice);
              setChoice('');
            }}
          >
            Grant
          </button>
        </div>
      ) : null}
    </div>
  );
}
