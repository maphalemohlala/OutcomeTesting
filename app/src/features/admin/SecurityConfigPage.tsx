import { useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Modal } from '../../components/feedback/Modal';
import { Tabs } from '../../components/navigation/Tabs';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { useSecurityConfig, type PagePermissionRow, type RoleMappingRow } from './useSecurityConfig';
import { useRoles, type RoleRow } from './useRoles';
import { createRole, updateRole } from '../../services/commands/roles';
import {
  assignUserRole,
  setPagePermission,
  setPermissionRuleActive,
  setRoleAssignmentActive,
} from '../../services/commands/permissions';
import { ACCESS_LEVELS, APP_ROLES, RESOURCE_KEYS, type AppRole } from '../../types/permissions';
import './SecurityConfigPage.css';

type Notice = { tone: 'ok' | 'error'; message: string } | null;

interface RoleOption {
  value: string;
  label: string;
  custom: boolean;
}

/** Splits the `builtin:Label` / `custom:CODE` select value into the two command arguments. */
function roleArgs(selected: string): { appRole?: string; roleCode?: string; label: string } {
  const value = selected.slice(selected.indexOf(':') + 1);
  return selected.startsWith('custom:')
    ? { roleCode: value, label: value }
    : { appRole: value, label: value };
}

export function SecurityConfigPage() {
  const { can } = usePermissions();
  const canManage = can('permission.manage', 'Manage');
  const [reloadKey, setReloadKey] = useState(0);
  const state = useSecurityConfig(reloadKey);
  const [rolesReloadKey, setRolesReloadKey] = useState(0);
  const roles = useRoles(rolesReloadKey);
  const intent = useIntentKeys();

  const [roleName, setRoleName] = useState('');
  const [roleDesc, setRoleDesc] = useState('');
  const [roleBusy, setRoleBusy] = useState(false);
  const [roleNotice, setRoleNotice] = useState<Notice>(null);
  const [roleOpen, setRoleOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleRow | null>(null);

  const [email, setEmail] = useState('');
  const [assignRole, setAssignRole] = useState<string>(`builtin:${APP_ROLES[0]}`);
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignNotice, setAssignNotice] = useState<Notice>(null);
  const [assignOpen, setAssignOpen] = useState(false);
  /** Set when the assign form is changing an existing assignment, so the old one is withdrawn. */
  const [replacing, setReplacing] = useState<RoleMappingRow | null>(null);

  const [permRole, setPermRole] = useState<string>(`builtin:${APP_ROLES[0]}`);
  const [resource, setResource] = useState<string>(RESOURCE_KEYS[0]);
  const [level, setLevel] = useState<string>(ACCESS_LEVELS[1]);
  const [permBusy, setPermBusy] = useState(false);
  const [permNotice, setPermNotice] = useState<Notice>(null);
  const [permOpen, setPermOpen] = useState(false);

  const [rowBusy, setRowBusy] = useState<string | null>(null);
  const [rowNotice, setRowNotice] = useState<Notice>(null);

  // Built-in roles use the al_approle picklist; custom roles (AD-044) enforce by al_role code.
  const roleOptions: RoleOption[] = [
    ...APP_ROLES.map((role) => ({ value: `builtin:${role}`, label: role, custom: false })),
    ...(roles.status === 'ready'
      ? roles.roles
          .filter((role) => role.active && !APP_ROLES.includes(role.name as AppRole))
          .map((role) => ({ value: `custom:${role.code}`, label: `${role.name} (custom)`, custom: true }))
      : []),
  ];

  function reloadConfig() {
    setReloadKey((k) => k + 1);
  }

  function openAssign(existing: RoleMappingRow | null) {
    setReplacing(existing);
    setEmail(existing ? existing.email : '');
    setAssignNotice(null);
    setAssignOpen(true);
  }

  async function onAssign(event: React.FormEvent) {
    event.preventDefault();
    if (!email.trim()) {
      setAssignNotice({ tone: 'error', message: 'Enter a work email.' });
      return;
    }
    setAssignBusy(true);
    setAssignNotice(null);

    const { appRole, roleCode, label } = roleArgs(assignRole);
    const token = `assign:${email.trim().toLowerCase()}:${label}`;
    const result = await assignUserRole({
      userEmail: email.trim(),
      appRole,
      roleCode,
      idempotencyKey: intent.keyFor(token),
    });

    if (!result.ok) {
      setAssignBusy(false);
      setAssignNotice({ tone: 'error', message: result.message });
      return;
    }
    intent.release(token);

    // Grant first, then withdraw: a failure here leaves the person with both roles rather
    // than none. The mapping's business code embeds the role, so a change is two writes.
    let message = `${label} assigned to ${email.trim()}.`;
    if (replacing && replacing.id !== result.data.MappingId) {
      const withdrawToken = `withdraw:${replacing.id}`;
      const withdrawn = await setRoleAssignmentActive({
        id: replacing.id,
        active: false,
        idempotencyKey: intent.keyFor(withdrawToken),
      });
      if (withdrawn.ok) {
        intent.release(withdrawToken);
        message = `${email.trim()} changed from ${replacing.role} to ${label}.`;
      } else {
        message = `${label} assigned, but the previous ${replacing.role} assignment could not be withdrawn: ${withdrawn.message}`;
      }
    }

    setAssignBusy(false);
    setAssignNotice({ tone: 'ok', message });
    setEmail('');
    setReplacing(null);
    setAssignOpen(false);
    reloadConfig();
  }

  function openPermission(existing: PagePermissionRow | null) {
    if (existing) {
      const known = APP_ROLES.includes(existing.role as AppRole);
      setPermRole(known ? `builtin:${existing.role}` : `custom:${existing.role}`);
      setResource(existing.resource);
      setLevel(existing.level);
    }
    setPermNotice(null);
    setPermOpen(true);
  }

  async function onSetPermission(event: React.FormEvent) {
    event.preventDefault();
    setPermBusy(true);
    setPermNotice(null);

    const { appRole, roleCode, label } = roleArgs(permRole);
    const token = `perm:${label}:${resource}:${level}`;
    const result = await setPagePermission({
      appRole,
      roleCode,
      resourceKey: resource,
      accessLevel: level,
      idempotencyKey: intent.keyFor(token),
    });
    setPermBusy(false);
    if (result.ok) {
      intent.release(token);
      setPermNotice({ tone: 'ok', message: `${label} now has ${level} on ${resource}.` });
      setPermOpen(false);
      reloadConfig();
    } else {
      setPermNotice({ tone: 'error', message: result.message });
    }
  }

  function openRoleForm(role: RoleRow | null) {
    setEditingRole(role);
    setRoleName(role ? role.name : '');
    setRoleDesc(role ? role.description ?? '' : '');
    setRoleNotice(null);
    setRoleOpen(true);
  }

  async function onSubmitRole(event: React.FormEvent) {
    event.preventDefault();
    if (!roleName.trim()) {
      setRoleNotice({ tone: 'error', message: 'Enter a role name.' });
      return;
    }
    setRoleBusy(true);
    setRoleNotice(null);

    const editing = editingRole;
    const token = editing ? `role:${editing.id}` : `role:new:${roleName.trim()}`;
    const result = editing
      ? await updateRole({
          roleId: editing.id,
          roleName: roleName.trim(),
          description: roleDesc.trim(),
          expectedRowVersion: editing.rowVersion,
          idempotencyKey: intent.keyFor(token),
        })
      : await createRole({
          roleName: roleName.trim(),
          description: roleDesc.trim() || null,
          idempotencyKey: intent.keyFor(token),
        });

    setRoleBusy(false);
    if (result.ok) {
      intent.release(token);
      setRoleNotice({
        tone: 'ok',
        message: editing ? `Role “${roleName.trim()}” updated.` : `Role “${roleName.trim()}” created.`,
      });
      setRoleName('');
      setRoleDesc('');
      setRoleOpen(false);
      setEditingRole(null);
      setRolesReloadKey((k) => k + 1);
      reloadConfig();
    } else {
      setRoleNotice({ tone: 'error', message: result.message });
    }
  }

  async function onSetAssignmentActive(row: RoleMappingRow) {
    if (rowBusy) return;
    setRowBusy(row.id);
    setRowNotice(null);
    const token = `assignment-active:${row.id}:${!row.active}`;
    const result = await setRoleAssignmentActive({
      id: row.id,
      active: !row.active,
      idempotencyKey: intent.keyFor(token),
    });
    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: `${row.role} ${row.active ? 'withdrawn from' : 'restored for'} ${row.email}.`,
      });
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
    }
  }

  async function onSetRuleActive(row: PagePermissionRow) {
    if (rowBusy) return;
    setRowBusy(row.id);
    setRowNotice(null);
    const token = `rule-active:${row.id}:${!row.active}`;
    const result = await setPermissionRuleActive({
      id: row.id,
      active: !row.active,
      idempotencyKey: intent.keyFor(token),
    });
    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: row.active
          ? `Override withdrawn; ${row.role} falls back to the default for ${row.resource}.`
          : `Override restored for ${row.role} on ${row.resource}.`,
      });
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
    }
  }

  async function onSetRoleActive(row: RoleRow) {
    if (rowBusy) return;
    setRowBusy(row.id);
    setRowNotice(null);
    const token = `role-active:${row.id}:${!row.active}`;
    const result = await updateRole({
      roleId: row.id,
      active: !row.active,
      expectedRowVersion: row.rowVersion,
      idempotencyKey: intent.keyFor(token),
    });
    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: row.active
          ? `${row.name} retired. Its assignments and permission rules were withdrawn with it.`
          : `${row.name} restored. Re-assign it to the people who need it.`,
      });
      setRolesReloadKey((k) => k + 1);
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
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
            <button type="button" className="security__btn" onClick={() => openAssign(null)}>
              Assign a role
            </button>
          </div>
          <p className="security__hint">
            Map a person&rsquo;s work email to an application role. People are registered on the{' '}
            <Link to="/admin/users">Users</Link> page.
          </p>
          {assignNotice && !assignOpen ? (
            <p className={`security__notice security__notice--${assignNotice.tone}`} role="status">
              {assignNotice.message}
            </p>
          ) : null}
        </section>

        <section className="security__panel" aria-labelledby="perm-heading">
          <div className="security__panel-bar">
            <h2 id="perm-heading">Page and capability permissions</h2>
            <button type="button" className="security__btn" onClick={() => openPermission(null)}>
              Set a permission
            </button>
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
              <button type="button" className="security__btn" onClick={() => openRoleForm(null)}>
                Create role
              </button>
            ) : null}
          </div>
          <p className="security__hint">
            The roles that can be assigned. Administrators can add, rename and retire them.
          </p>
          {roleNotice && !roleOpen ? (
            <p className={`security__notice security__notice--${roleNotice.tone}`} role="status">
              {roleNotice.message}
            </p>
          ) : null}
        </section>
      </div>

      {assignOpen ? (
        <Modal
          title={replacing ? `Change role for ${replacing.email}` : 'Assign a role'}
          onClose={() => {
            setAssignOpen(false);
            setReplacing(null);
          }}
        >
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
                readOnly={replacing !== null}
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
            {replacing ? (
              <p className="security__hint">
                The {replacing.role} assignment is withdrawn once the new role is granted. The
                withdrawn record is kept for the audit trail.
              </p>
            ) : null}
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => {
                  setAssignOpen(false);
                  setReplacing(null);
                }}
                disabled={assignBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={assignBusy}>
                {assignBusy ? 'Saving…' : replacing ? 'Change role' : 'Assign role'}
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
            <p className="security__hint">
              Setting a level for a role and resource replaces any existing rule for that pair.
            </p>
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
        <Modal
          title={editingRole ? `Edit ${editingRole.name}` : 'Create a role'}
          onClose={() => {
            setRoleOpen(false);
            setEditingRole(null);
          }}
        >
          {roleNotice ? (
            <p className={`security__notice security__notice--${roleNotice.tone}`} role="status">
              {roleNotice.message}
            </p>
          ) : null}
          <form className="security__form" onSubmit={onSubmitRole}>
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
            {editingRole ? (
              <p className="security__hint">
                The role code <strong>{editingRole.code}</strong> stays the same, so existing
                assignments and permission rules keep working.
              </p>
            ) : null}
            <div className="security__form-actions">
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={() => {
                  setRoleOpen(false);
                  setEditingRole(null);
                }}
                disabled={roleBusy}
              >
                Cancel
              </button>
              <button type="submit" className="security__btn" disabled={roleBusy}>
                {roleBusy ? 'Saving…' : editingRole ? 'Save changes' : 'Create role'}
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

      {rowNotice ? (
        <p className={`security__notice security__notice--${rowNotice.tone}`} role="status">
          {rowNotice.message}
        </p>
      ) : null}

      <Tabs
        label="Security configuration"
        items={[
          {
            id: 'assignments',
            label: 'Role assignments',
            count: state.status === 'ready' ? state.mappings.length : undefined,
            render: () =>
              state.status !== 'ready' ? (
                <p role="status">
                  {state.status === 'loading'
                    ? 'Loading role assignments…'
                    : 'Role assignments cannot be shown.'}
                </p>
              ) : state.mappings.length === 0 ? (
                <p>
                  No roles assigned yet. Everyone has full access until the first role is assigned.
                </p>
              ) : (
                <table className="security__table">
                  <thead>
                    <tr>
                      <th scope="col">Work email</th>
                      <th scope="col">Role</th>
                      <th scope="col">Status</th>
                      {canManage ? <th scope="col">Actions</th> : null}
                    </tr>
                  </thead>
                  <tbody>
                    {state.mappings.map((m) => (
                      <tr key={m.id} data-inactive={m.active ? undefined : 'true'}>
                        <td>{m.email}</td>
                        <td>{m.role}</td>
                        <td>{m.active ? 'Active' : 'Withdrawn'}</td>
                        {canManage ? (
                          <td className="security__row-actions">
                            {m.active ? (
                              <button
                                type="button"
                                className="security__link-btn"
                                onClick={() => openAssign(m)}
                                disabled={rowBusy !== null}
                              >
                                Change role
                              </button>
                            ) : null}
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => onSetAssignmentActive(m)}
                              disabled={rowBusy !== null}
                            >
                              {rowBusy === m.id ? 'Working…' : m.active ? 'Withdraw' : 'Restore'}
                            </button>
                          </td>
                        ) : null}
                      </tr>
                    ))}
                  </tbody>
                </table>
              ),
          },
          {
            id: 'permissions',
            label: 'Permission rules',
            count: state.status === 'ready' ? state.permissions.length : undefined,
            render: () =>
              state.status !== 'ready' ? (
                <p role="status">
                  {state.status === 'loading'
                    ? 'Loading permission rules…'
                    : 'Permission rules cannot be shown.'}
                </p>
              ) : state.permissions.length === 0 ? (
                <p>No overrides set. The built-in role defaults apply.</p>
              ) : (
                <table className="security__table">
                  <thead>
                    <tr>
                      <th scope="col">Resource</th>
                      <th scope="col">Role</th>
                      <th scope="col">Access</th>
                      <th scope="col">Status</th>
                      {canManage ? <th scope="col">Actions</th> : null}
                    </tr>
                  </thead>
                  <tbody>
                    {state.permissions.map((p) => (
                      <tr key={p.id} data-inactive={p.active ? undefined : 'true'}>
                        <td>{p.resource}</td>
                        <td>{p.role}</td>
                        <td>{p.level}</td>
                        <td>{p.active ? 'Active' : 'Withdrawn'}</td>
                        {canManage ? (
                          <td className="security__row-actions">
                            {p.active ? (
                              <button
                                type="button"
                                className="security__link-btn"
                                onClick={() => openPermission(p)}
                                disabled={rowBusy !== null}
                              >
                                Change level
                              </button>
                            ) : null}
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => onSetRuleActive(p)}
                              disabled={rowBusy !== null}
                            >
                              {rowBusy === p.id ? 'Working…' : p.active ? 'Withdraw' : 'Restore'}
                            </button>
                          </td>
                        ) : null}
                      </tr>
                    ))}
                  </tbody>
                </table>
              ),
          },
          {
            id: 'roles',
            label: 'Role registry',
            count: roles.status === 'ready' ? roles.roles.length : undefined,
            render: () =>
              roles.status === 'loading' ? (
                <p role="status">Loading roles…</p>
              ) : roles.status === 'unavailable' ? (
                <p>{roles.reason}</p>
              ) : roles.roles.length === 0 ? (
                <p>No roles have been created yet.</p>
              ) : (
                <table className="security__table">
                  <thead>
                    <tr>
                      <th scope="col">Role</th>
                      <th scope="col">Code</th>
                      <th scope="col">Description</th>
                      <th scope="col">Active</th>
                      {canManage ? <th scope="col">Actions</th> : null}
                    </tr>
                  </thead>
                  <tbody>
                    {roles.roles.map((r) => (
                      <tr key={r.id} data-inactive={r.active ? undefined : 'true'}>
                        <td>{r.name}</td>
                        <td>{r.code}</td>
                        <td>{r.description ?? '—'}</td>
                        <td>{r.active ? 'Yes' : 'No'}</td>
                        {canManage ? (
                          <td className="security__row-actions">
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => openRoleForm(r)}
                              disabled={rowBusy !== null}
                            >
                              Edit
                            </button>
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => onSetRoleActive(r)}
                              disabled={rowBusy !== null}
                            >
                              {rowBusy === r.id ? 'Working…' : r.active ? 'Retire' : 'Restore'}
                            </button>
                          </td>
                        ) : null}
                      </tr>
                    ))}
                  </tbody>
                </table>
              ),
          },
        ]}
      />
    </>
  );
}
