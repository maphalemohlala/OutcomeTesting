import { useState } from 'react';
import { canWithdraw, mappingFor } from './roleAssignment';
import type { RoleMappingRow } from '../admin/useSecurityConfig';
import { Icon } from '../../components/icons/Icon';

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
 * Withdraw beside a role the Grant dropdown cannot offer back is a one-way door, so those
 * chips carry no × and the page note says where they are managed (2026-09-22 review).
 *
 * "+ Add role" only reveals the grant picker; nothing is written until Grant is pressed.
 * This writes al_userrolemapping, which PermissionHelpers reads when it decides what a
 * caller may do — an administrator tagging somebody "Adviser" for reporting is also
 * letting them in, and the picker's label says so.
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
  const [adding, setAdding] = useState(false);

  if (email === '') {
    // Someone named on a case who is not in the registry. There is no mapping to hold,
    // because al_userrolemapping is keyed on work email.
    return <NotInDirectory />;
  }

  const offered = grantable.filter((role) => !mappingFor(mappings, email, role));

  return (
    <ul className="people__role-list">
      {roles.length === 0 ? (
        <li className="people__role-chip people__role-chip--warn">
          <Icon name="warning" />
          No roles yet
        </li>
      ) : null}
      {roles.map((role) => {
        const held = mappingFor(mappings, email, role);
        return (
          // Administrators is tinted apart: it is the role that can change everyone else's.
          <li
            key={role}
            className="people__role-chip"
            data-privileged={role === 'Administrators' || undefined}
          >
            <span>{role.replace(/^AL Portal - /, '')}</span>
            {canManage && held && canWithdraw(role, grantable) ? (
              <button
                type="button"
                className="people__role-withdraw"
                aria-label={`Withdraw ${role}`}
                title="Withdraw"
                disabled={busy}
                onClick={() => onWithdraw(held.id, email, role)}
              >
                <Icon name="close" />
              </button>
            ) : null}
          </li>
        );
      })}

      {canManage && offered.length > 0 ? (
        <li className="people__role-grant">
          {adding ? (
            <>
              <label className="visually-hidden" htmlFor={`grant-${email}`}>
                Grant a role to {email}. A role grants access to this application.
              </label>
              <select
                id={`grant-${email}`}
                value={choice}
                disabled={busy}
                // Opened by the user's own click on "+ Add role", so focus follows them here.
                autoFocus
                onChange={(event) => setChoice(event.target.value)}
              >
                <option value="">Choose a role&hellip;</option>
                {offered.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
              <button
                type="button"
                className="people__role-grant-go"
                disabled={busy || choice === '' || !grantable.includes(choice)}
                onClick={() => {
                  onGrant(email, choice);
                  setChoice('');
                  setAdding(false);
                }}
              >
                Grant
              </button>
              <button
                type="button"
                className="people__role-grant-cancel"
                onClick={() => {
                  setChoice('');
                  setAdding(false);
                }}
              >
                Cancel
              </button>
            </>
          ) : (
            <button
              type="button"
              className="people__role-add"
              disabled={busy}
              aria-label={`Add a role for ${email}`}
              onClick={() => setAdding(true)}
            >
              <Icon name="plus" />
              Add role
            </button>
          )}
        </li>
      ) : null}
    </ul>
  );
}

/** Named on a case but not a contact, so nothing can be granted until they are added. */
export function NotInDirectory() {
  return (
    <span className="people__role-chip people__role-chip--danger">
      <Icon name="userX" />
      Not in the directory
    </span>
  );
}
