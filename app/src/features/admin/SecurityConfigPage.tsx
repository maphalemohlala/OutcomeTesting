import { useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { Modal } from '../../components/feedback/Modal';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useSecurityConfig } from './useSecurityConfig';
import { useRoles } from './useRoles';
import { useUsers } from './useUsers';
import { createRole, newRoleIntentKey } from '../../services/commands/roles';
import { createUser, newUserIntentKey } from '../../services/commands/users';
import {
  assignUserRole,
  newPermissionIntentKey,
  setPagePermission,
} from '../../services/commands/permissions';
import { ACCESS_LEVELS, APP_ROLES, RESOURCE_KEYS, type AppRole } from '../../types/permissions';
import './SecurityConfigPage.css';

type Notice = { tone: 'ok' | 'error'; message: string } | null;

interface RoleOption {
  value: string;
  label: string;
  custom: boolean;
}

export function SecurityConfigPage() {
  const { can, ready } = usePermissions();
  // `ready` gates this as well as the level: the provider stands a permissive set in
  // while it resolves, so without it the privileged controls would be offered to
  // everyone for the length of that read (AD-041 keeps the server the real gate).
  const canManage = ready && can('permission.manage', 'Manage');
  const [reloadKey, setReloadKey] = useState(0);
  const state = useSecurityConfig(reloadKey);
  const [rolesReloadKey, setRolesReloadKey] = useState(0);
  const roles = useRoles(rolesReloadKey);
  const [usersReloadKey, setUsersReloadKey] = useState(0);
  const users = useUsers(usersReloadKey);

  const [userName, setUserName] = useState('');
  const [userEmail, setUserEmail] = useState('');
  const [userBusy, setUserBusy] = useState(false);
  const [userNotice, setUserNotice] = useState<Notice>(null);
  const [userOpen, setUserOpen] = useState(false);

  const [roleName, setRoleName] = useState('');
  const [roleDesc, setRoleDesc] = useState('');
  const [roleBusy, setRoleBusy] = useState(false);
  const [roleNotice, setRoleNotice] = useState<Notice>(null);
  const [roleOpen, setRoleOpen] = useState(false);

  const [email, setEmail] = useState('');
  const [assignRole, setAssignRole] = useState<string>(`builtin:${APP_ROLES[0]}`);
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignNotice, setAssignNotice] = useState<Notice>(null);
  const [assignOpen, setAssignOpen] = useState(false);

  const [permRole, setPermRole] = useState<string>(`builtin:${APP_ROLES[0]}`);
  const [resource, setResource] = useState<string>(RESOURCE_KEYS[0]);
  const [level, setLevel] = useState<string>(ACCESS_LEVELS[1]);
  const [permBusy, setPermBusy] = useState(false);
  const [permNotice, setPermNotice] = useState<Notice>(null);
  const [permOpen, setPermOpen] = useState(false);

  // Built-in roles use the al_approle picklist; custom roles (AD-044) enforce by al_role code.
  const roleOptions: RoleOption[] = [
    ...APP_ROLES.map((role) => ({ value: `builtin:${role}`, label: role, custom: false })),
    ...(roles.status === 'ready'
      ? roles.roles
          .filter((role) => role.active && !APP_ROLES.includes(role.name as AppRole))
          .map((role) => ({ value: `custom:${role.code}`, label: `${role.name} (custom)`, custom: true }))
      : []),
  ];

  async function onAssign(event: React.FormEvent) {
    event.preventDefault();
    if (!email.trim()) {
      setAssignNotice({ tone: 'error', message: 'Enter a work email.' });
      return;
    }
    setAssignBusy(true);
    setAssignNotice(null);
    const custom = assignRole.startsWith('custom:');
    const roleValue = assignRole.slice(assignRole.indexOf(':') + 1);
    const result = await assignUserRole({
      userEmail: email.trim(),
      appRole: custom ? undefined : roleValue,
      roleCode: custom ? roleValue : undefined,
      idempotencyKey: newPermissionIntentKey(),
    });
    setAssignBusy(false);
    if (result.ok) {
      setAssignNotice({ tone: 'ok', message: `${roleValue} assigned to ${email.trim()}.` });
      setEmail('');
      setAssignOpen(false);
      setReloadKey((k) => k + 1);
    } else {
      setAssignNotice({ tone: 'error', message: result.message });
    }
  }

  async function onSetPermission(event: React.FormEvent) {
    event.preventDefault();
    setPermBusy(true);
    setPermNotice(null);
    const custom = permRole.startsWith('custom:');
    const roleValue = permRole.slice(permRole.indexOf(':') + 1);
    const result = await setPagePermission({
      appRole: custom ? undefined : roleValue,
      roleCode: custom ? roleValue : undefined,
      resourceKey: resource,
      accessLevel: level,
      idempotencyKey: newPermissionIntentKey(),
    });
    setPermBusy(false);
    if (result.ok) {
      setPermNotice({ tone: 'ok', message: `${roleValue} now has ${level} on ${resource}.` });
      setPermOpen(false);
      setReloadKey((k) => k + 1);
    } else {
      setPermNotice({ tone: 'error', message: result.message });
    }
  }

  async function onCreateUser(event: React.FormEvent) {
    event.preventDefault();
    if (!userName.trim()) {
      setUserNotice({ tone: 'error', message: 'Enter the person’s full name.' });
      return;
    }
    if (!userEmail.trim()) {
      setUserNotice({ tone: 'error', message: 'Enter a work email.' });
      return;
    }
    setUserBusy(true);
    setUserNotice(null);
    const result = await createUser({
      fullName: userName.trim(),
      workEmail: userEmail.trim(),
      idempotencyKey: newUserIntentKey(),
    });
    setUserBusy(false);
    if (result.ok) {
      setUserNotice({ tone: 'ok', message: `${userName.trim()} added to the user registry.` });
      setUserName('');
      setUserEmail('');
      setUserOpen(false);
      setUsersReloadKey((k) => k + 1);
    } else {
      setUserNotice({ tone: 'error', message: result.message });
    }
  }

  async function onCreateRole(event: React.FormEvent) {
    event.preventDefault();
    if (!roleName.trim()) {
      setRoleNotice({ tone: 'error', message: 'Enter a role name.' });
      return;
    }
    setRoleBusy(true);
    setRoleNotice(null);
    const result = await createRole({
      roleName: roleName.trim(),
      description: roleDesc.trim() || null,
      idempotencyKey: newRoleIntentKey(),
    });
    setRoleBusy(false);
    if (result.ok) {
      setRoleNotice({ tone: 'ok', message: `Role “${roleName.trim()}” created.` });
      setRoleName('');
      setRoleDesc('');
      setRoleOpen(false);
      setRolesReloadKey((k) => k + 1);
    } else {
      setRoleNotice({ tone: 'error', message: result.message });
    }
  }

  return (
    <>
      <PageIntro
        title="Security configuration"
        purpose="Assign application roles to people and set what each role can see and do. Changes are enforced server-side and recorded in the audit trail (AD-041)."
      />

      <div className="security">
        <section className="security__panel" aria-labelledby="assign-heading">
          <div className="security__panel-bar">
            <h2 id="assign-heading">Assign a role</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => {
                  setAssignNotice(null);
                  setAssignOpen(true);
                }}
              >
                Assign a role
              </button>
            ) : null}
          </div>
          <p className="security__hint">Map a person&rsquo;s work email to an application role.</p>
          {assignNotice && !assignOpen ? (
            <p className={`security__notice security__notice--${assignNotice.tone}`} role="status">
              {assignNotice.message}
            </p>
          ) : null}
        </section>

        <section className="security__panel" aria-labelledby="perm-heading">
          <div className="security__panel-bar">
            <h2 id="perm-heading">Page and capability permissions</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => {
                  setPermNotice(null);
                  setPermOpen(true);
                }}
              >
                Set a permission
              </button>
            ) : null}
          </div>
          <p className="security__hint">Grant a role an access level on a page or capability.</p>
          {permNotice && !permOpen ? (
            <p className={`security__notice security__notice--${permNotice.tone}`} role="status">
              {permNotice.message}
            </p>
          ) : null}
        </section>

        <section className="security__panel" aria-labelledby="roles-heading">
          <div className="security__panel-bar">
            <h2 id="roles-heading">Roles</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => {
                  setRoleNotice(null);
                  setRoleOpen(true);
                }}
              >
                Create role
              </button>
            ) : null}
          </div>
          <p className="security__hint">
            The roles that can be assigned. Administrators can add new ones.
          </p>
          {roleNotice && !roleOpen ? (
            <p className={`security__notice security__notice--${roleNotice.tone}`} role="status">
              {roleNotice.message}
            </p>
          ) : null}
        </section>

        <section className="security__panel" aria-labelledby="users-heading">
          <div className="security__panel-bar">
            <h2 id="users-heading">People</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => {
                  setUserNotice(null);
                  setUserOpen(true);
                }}
              >
                Register user
              </button>
            ) : null}
          </div>
          <p className="security__hint">
            The people known to the application, keyed on work email. Administrators can add new ones.
          </p>
          {userNotice && !userOpen ? (
            <p className={`security__notice security__notice--${userNotice.tone}`} role="status">
              {userNotice.message}
            </p>
          ) : null}
        </section>
      </div>

      {assignOpen ? (
        <Modal title="Assign a role" onClose={() => setAssignOpen(false)}>
          {assignNotice ? (
            <p className={`security__notice security__notice--${assignNotice.tone}`} role="status">
              {assignNotice.message}
            </p>
          ) : null}
          <form className="security__form" onSubmit={onAssign}>
            <label className="security__field">
              <span>Work email</span>
              <input
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                placeholder="person@ascotlloyd.co.uk"
                autoComplete="off"
              />
            </label>
            <label className="security__field">
              <span>Role</span>
              <select value={assignRole} onChange={(e) => setAssignRole(e.target.value)}>
                {roleOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => setAssignOpen(false)}
                disabled={assignBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={assignBusy}>
                {assignBusy ? 'Assigning…' : 'Assign role'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}

      {permOpen ? (
        <Modal title="Set a page or capability permission" onClose={() => setPermOpen(false)}>
          {permNotice ? (
            <p className={`security__notice security__notice--${permNotice.tone}`} role="status">
              {permNotice.message}
            </p>
          ) : null}
          <form className="security__form" onSubmit={onSetPermission}>
            <label className="security__field">
              <span>Role</span>
              <select value={permRole} onChange={(e) => setPermRole(e.target.value)}>
                {roleOptions.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </label>
            <label className="security__field">
              <span>Resource</span>
              <select value={resource} onChange={(e) => setResource(e.target.value)}>
                {RESOURCE_KEYS.map((key) => (
                  <option key={key} value={key}>
                    {key}
                  </option>
                ))}
              </select>
            </label>
            <label className="security__field">
              <span>Access level</span>
              <select value={level} onChange={(e) => setLevel(e.target.value)}>
                {ACCESS_LEVELS.map((lvl) => (
                  <option key={lvl} value={lvl}>
                    {lvl}
                  </option>
                ))}
              </select>
            </label>
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => setPermOpen(false)}
                disabled={permBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={permBusy}>
                {permBusy ? 'Saving…' : 'Set permission'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}

      {roleOpen ? (
        <Modal title="Create a role" onClose={() => setRoleOpen(false)}>
          {roleNotice ? (
            <p className={`security__notice security__notice--${roleNotice.tone}`} role="status">
              {roleNotice.message}
            </p>
          ) : null}
          <form className="security__form" onSubmit={onCreateRole}>
            <label className="security__field">
              <span>Role name</span>
              <input
                type="text"
                value={roleName}
                onChange={(e) => setRoleName(e.target.value)}
                placeholder="e.g. Senior Checker"
                autoComplete="off"
              />
            </label>
            <label className="security__field">
              <span>Description</span>
              <textarea
                value={roleDesc}
                onChange={(e) => setRoleDesc(e.target.value)}
                rows={2}
                placeholder="What the role is for"
              />
            </label>
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => setRoleOpen(false)}
                disabled={roleBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={roleBusy}>
                {roleBusy ? 'Creating…' : 'Create role'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}

      {userOpen ? (
        <Modal title="Register a user" onClose={() => setUserOpen(false)}>
          {userNotice ? (
            <p className={`security__notice security__notice--${userNotice.tone}`} role="status">
              {userNotice.message}
            </p>
          ) : null}
          <form className="security__form" onSubmit={onCreateUser}>
            <label className="security__field">
              <span>Full name</span>
              <input
                type="text"
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                placeholder="e.g. Jordan Taylor"
                autoComplete="off"
              />
            </label>
            <label className="security__field">
              <span>Work email</span>
              <input
                type="email"
                value={userEmail}
                onChange={(e) => setUserEmail(e.target.value)}
                placeholder="person@ascotlloyd.co.uk"
                autoComplete="off"
              />
            </label>
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => setUserOpen(false)}
                disabled={userBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={userBusy}>
                {userBusy ? 'Registering…' : 'Register user'}
              </button>
            </div>
          </form>
        </Modal>
      ) : null}

      {state.status === 'loading' ? <p role="status">Loading security configuration…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="security__unavailable" aria-labelledby="security-unavailable">
          <h2 id="security-unavailable">Configuration cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        <div className="security__tables">
          <section aria-labelledby="mappings-heading">
            <h2 id="mappings-heading">Role assignments</h2>
            {state.mappings.length === 0 ? (
              <p>No roles assigned yet. Everyone has full access until the first role is assigned.</p>
            ) : (
              <table className="security__table">
                <thead>
                  <tr>
                    <th scope="col">Work email</th>
                    <th scope="col">Role</th>
                  </tr>
                </thead>
                <tbody>
                  {state.mappings.map((m) => (
                    <tr key={m.id}>
                      <td>{m.email}</td>
                      <td>{m.role}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          <section aria-labelledby="perms-heading">
            <h2 id="perms-heading">Permission rules</h2>
            {state.permissions.length === 0 ? (
              <p>No overrides set. The built-in role defaults apply.</p>
            ) : (
              <table className="security__table">
                <thead>
                  <tr>
                    <th scope="col">Resource</th>
                    <th scope="col">Role</th>
                    <th scope="col">Access</th>
                  </tr>
                </thead>
                <tbody>
                  {state.permissions.map((p) => (
                    <tr key={p.id}>
                      <td>{p.resource}</td>
                      <td>{p.role}</td>
                      <td>{p.level}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>
        </div>
      ) : null}

      <div className="security__tables">
        <section aria-labelledby="roles-list-heading">
          <h2 id="roles-list-heading">Role registry</h2>
          {roles.status === 'loading' ? <p role="status">Loading roles…</p> : null}
          {roles.status === 'unavailable' ? <p>{roles.reason}</p> : null}
          {roles.status === 'ready' ? (
            roles.roles.length === 0 ? (
              <p>No roles have been created yet.</p>
            ) : (
              <table className="security__table">
                <thead>
                  <tr>
                    <th scope="col">Role</th>
                    <th scope="col">Code</th>
                    <th scope="col">Description</th>
                    <th scope="col">Active</th>
                  </tr>
                </thead>
                <tbody>
                  {roles.roles.map((r) => (
                    <tr key={r.id}>
                      <td>{r.name}</td>
                      <td>{r.code}</td>
                      <td>{r.description ?? '—'}</td>
                      <td>{r.active ? 'Yes' : 'No'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )
          ) : null}
        </section>

        <section aria-labelledby="users-list-heading">
          <h2 id="users-list-heading">User registry</h2>
          {users.status === 'loading' ? <p role="status">Loading users…</p> : null}
          {users.status === 'unavailable' ? <p>{users.reason}</p> : null}
          {users.status === 'ready' ? (
            users.users.length === 0 ? (
              <p>No users have been registered yet.</p>
            ) : (
              <table className="security__table">
                <thead>
                  <tr>
                    <th scope="col">Name</th>
                    <th scope="col">Work email</th>
                    <th scope="col">Active</th>
                  </tr>
                </thead>
                <tbody>
                  {users.users.map((u) => (
                    <tr key={u.id}>
                      <td>{u.name}</td>
                      <td>{u.email}</td>
                      <td>{u.active ? 'Yes' : 'No'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )
          ) : null}
        </section>
      </div>
    </>
  );
}
