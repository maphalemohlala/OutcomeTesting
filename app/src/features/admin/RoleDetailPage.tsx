import { useMemo, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { updateRole } from '../../services/commands/roles';
import {
  assignUserRole,
  setPagePermission,
  setPermissionRuleActive,
} from '../../services/commands/permissions';
import { adoptRoleAssignment } from '../../services/commands/roleReconciliation';
import { ACCESS_LEVELS, RESOURCE_KEYS } from '../../types/permissions';
import { useRoles, type RoleRow } from './useRoles';
import { useSecurityConfig } from './useSecurityConfig';
import { useRoleHolders } from './useRoleHolders';
import { buildRoleGrants, classifyHolder, sameRoleCode, type RoleGrant, type RoleHolderRecord } from './roleDetail';
import {
  AssignRoleModal,
  PermissionModal,
  RoleFormModal,
  type Notice,
  type RoleOption,
} from './SecurityModals';
import './SecurityConfigPage.css';

function NotFound({ code }: { code: string }) {
  return (
    <>
      <PageIntro
        title="Role not found"
        purpose={`No role is registered under “${code}”. It may have been renamed, or the link may be out of date.`}
      />
      <p>
        <Link to="/admin/security">Back to security configuration</Link>
      </p>
    </>
  );
}

/**
 * One role, end to end: what it grants and who holds it (AD-041, AD-044).
 *
 * The security configuration screen lists roles, assignments and permission rules as three
 * tables, so answering "what does this role actually grant, and who holds it" meant reading
 * across all three. This is that reading, done for one role, with the same edit and withdraw
 * actions the tables carry.
 *
 * It adds no data source: the two hooks the parent screen already uses are filtered to this
 * role in the client.
 */
export function RoleDetailPage() {
  const { roleCode = '' } = useParams<{ roleCode: string }>();
  const { can, ready } = usePermissions();
  // `ready` gates this as well as the level, for the reason the parent screen does: the
  // provider stands a permissive set in while it resolves (AD-041 keeps the server the
  // real gate).
  const canManage = ready && can('permission.manage', 'Manage');

  const [reloadKey, setReloadKey] = useState(0);
  const [rolesReloadKey, setRolesReloadKey] = useState(0);
  const config = useSecurityConfig(reloadKey);
  const roles = useRoles(rolesReloadKey);
  const intent = useIntentKeys();

  const [rowBusy, setRowBusy] = useState<string | null>(null);
  const [rowNotice, setRowNotice] = useState<Notice>(null);

  const [roleOpen, setRoleOpen] = useState(false);
  const [roleName, setRoleName] = useState('');
  const [roleDesc, setRoleDesc] = useState('');
  const [roleBusy, setRoleBusy] = useState(false);
  const [roleNotice, setRoleNotice] = useState<Notice>(null);

  const [assignOpen, setAssignOpen] = useState(false);
  const [email, setEmail] = useState('');
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignNotice, setAssignNotice] = useState<Notice>(null);

  const [permOpen, setPermOpen] = useState(false);
  const [resource, setResource] = useState<string>(RESOURCE_KEYS[0]);
  const [level, setLevel] = useState<string>(ACCESS_LEVELS[1]);
  const [permBusy, setPermBusy] = useState(false);
  const [permNotice, setPermNotice] = useState<Notice>(null);

  const role: RoleRow | null = useMemo(() => {
    if (roles.status !== 'ready') return null;
    return roles.roles.find((candidate) => sameRoleCode(candidate.code, roleCode)) ?? null;
  }, [roles, roleCode]);

  const grants = useMemo(
    () => (role && config.status === 'ready' ? buildRoleGrants(role.code, config.permissions) : []),
    [role, config],
  );

  // The role code is not known until `role` resolves, but this hook must stay above the
  // early returns below (loading / unavailable / not-found) for the rule of hooks — so it
  // runs with an empty code until then, and useRoleHolders no-ops on that (AD-089).
  const holdersState = useRoleHolders(role?.code ?? '', reloadKey);
  const holders = holdersState.status === 'ready' ? holdersState.holders : [];

  function reloadConfig() {
    setReloadKey((key) => key + 1);
  }

  if (roles.status === 'loading') {
    return <p role="status">Loading role…</p>;
  }

  if (roles.status === 'unavailable') {
    return (
      <>
        <PageIntro title="Role" purpose="This role cannot be shown." />
        <section className="security__unavailable" aria-labelledby="role-unavailable">
          <h2 id="role-unavailable">Role cannot be shown</h2>
          <p>{roles.reason}</p>
        </section>
      </>
    );
  }

  if (!role) {
    return <NotFound code={roleCode} />;
  }

  // The role is the subject of this page, so every form here is scoped to it: a single
  // option states the role rather than offering a choice of one.
  const roleOptions: RoleOption[] = [
    { value: `custom:${role.code}`, label: role.name, custom: true },
  ];

  function openPermission(grant: RoleGrant | null) {
    setResource(grant ? grant.resource : RESOURCE_KEYS[0]);
    setLevel(grant ? grant.level : ACCESS_LEVELS[1]);
    setPermNotice(null);
    setPermOpen(true);
  }

  async function onSetPermission(event: React.FormEvent) {
    event.preventDefault();
    if (!role) return;
    setPermBusy(true);
    setPermNotice(null);

    const token = `perm:${role.code}:${resource}:${level}`;
    const result = await setPagePermission({
      roleCode: role.code,
      resourceKey: resource,
      accessLevel: level,
      idempotencyKey: intent.keyFor(token),
    });

    setPermBusy(false);
    if (result.ok) {
      intent.release(token);
      setPermNotice({ tone: 'ok', message: `${role.name} now has ${level} on ${resource}.` });
      setPermOpen(false);
      reloadConfig();
    } else {
      setPermNotice({ tone: 'error', message: result.message });
    }
  }

  async function onWithdrawOverride(grant: RoleGrant) {
    if (rowBusy || !grant.overrideId) return;
    setRowBusy(grant.overrideId);
    setRowNotice(null);

    const token = `rule-active:${grant.overrideId}:false`;
    const result = await setPermissionRuleActive({
      id: grant.overrideId,
      active: false,
      idempotencyKey: intent.keyFor(token),
    });

    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: `Override withdrawn; ${role?.name} falls back to the default for ${grant.resource}.`,
      });
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
    }
  }

  async function onAssign(event: React.FormEvent) {
    event.preventDefault();
    if (!role) return;
    if (!email.trim()) {
      setAssignNotice({ tone: 'error', message: 'Select the person to assign this role to.' });
      return;
    }
    setAssignBusy(true);
    setAssignNotice(null);

    const token = `assign:${email.trim().toLowerCase()}:${role.code}`;
    const result = await assignUserRole({
      userEmail: email.trim(),
      roleCode: role.code,
      idempotencyKey: intent.keyFor(token),
    });

    setAssignBusy(false);
    if (result.ok) {
      intent.release(token);
      setAssignNotice({ tone: 'ok', message: `${role.name} assigned to ${email.trim()}.` });
      setEmail('');
      setAssignOpen(false);
      reloadConfig();
    } else {
      setAssignNotice({ tone: 'error', message: result.message });
    }
  }

  async function onReconcile(holder: RoleHolderRecord, decision: 'Adopt' | 'Revoke') {
    if (rowBusy || !role) return;
    setRowBusy(holder.email);
    setRowNotice(null);

    const token = `adopt:${holder.email}:${role.code}:${decision}`;
    const result = await adoptRoleAssignment({
      userEmail: holder.email,
      roleCode: role.code,
      decision,
      idempotencyKey: intent.keyFor(token),
    });

    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: decision === 'Adopt'
          ? `${role.name} adopted for ${holder.email}. The decision is now in the audit trail.`
          : `${role.name} revoked for ${holder.email} in both Power Pages and this app.`,
      });
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
    }
  }

  function openRoleForm() {
    if (!role) return;
    setRoleName(role.name);
    setRoleDesc(role.description ?? '');
    setRoleNotice(null);
    setRoleOpen(true);
  }

  async function onSubmitRole(event: React.FormEvent) {
    event.preventDefault();
    if (!role) return;
    if (!roleName.trim()) {
      setRoleNotice({ tone: 'error', message: 'Enter a role name.' });
      return;
    }
    setRoleBusy(true);
    setRoleNotice(null);

    const token = `role:${role.id}`;
    const result = await updateRole({
      roleId: role.id,
      roleName: roleName.trim(),
      description: roleDesc.trim(),
      expectedRowVersion: role.rowVersion,
      idempotencyKey: intent.keyFor(token),
    });

    setRoleBusy(false);
    if (result.ok) {
      intent.release(token);
      setRoleNotice({ tone: 'ok', message: `Role “${roleName.trim()}” updated.` });
      setRoleOpen(false);
      setRolesReloadKey((key) => key + 1);
      reloadConfig();
    } else {
      setRoleNotice({ tone: 'error', message: result.message });
    }
  }

  async function onSetRoleActive() {
    if (rowBusy || !role) return;
    setRowBusy(role.id);
    setRowNotice(null);

    const token = `role-active:${role.id}:${!role.active}`;
    const result = await updateRole({
      roleId: role.id,
      active: !role.active,
      expectedRowVersion: role.rowVersion,
      idempotencyKey: intent.keyFor(token),
    });

    setRowBusy(null);
    if (result.ok) {
      intent.release(token);
      setRowNotice({
        tone: 'ok',
        message: role.active
          ? `${role.name} retired. Its assignments and permission rules were withdrawn with it.`
          : `${role.name} restored. Re-assign it to the people who need it.`,
      });
      setRolesReloadKey((key) => key + 1);
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: result.message });
    }
  }

  const loading = config.status === 'loading';

  return (
    <>
      <PageIntro
        title={role.name}
        purpose={
          role.description?.trim() ||
          'What this role grants and who holds it. Changes are enforced server-side and recorded in the audit trail (AD-041).'
        }
        actions={
          canManage ? (
            <div className="security__row-actions">
              <button type="button" className="security__btn" onClick={openRoleForm}>
                Edit role
              </button>
              <button
                type="button"
                className="security__btn security__btn--ghost"
                onClick={onSetRoleActive}
                disabled={rowBusy !== null}
              >
                {rowBusy === role.id ? 'Working…' : role.active ? 'Retire role' : 'Restore role'}
              </button>
            </div>
          ) : null
        }
      />

      <p className="security__hint">
        <Link to="/admin/security">Back to security configuration</Link> · Role code{' '}
        <strong>{role.code}</strong> · {role.active ? 'Active' : 'Retired'}
      </p>

      {roleNotice && !roleOpen ? (
        <p className={`security__notice security__notice--${roleNotice.tone}`} role="status">
          {roleNotice.message}
        </p>
      ) : null}
      {rowNotice ? (
        <p className={`security__notice security__notice--${rowNotice.tone}`} role="status">
          {rowNotice.message}
        </p>
      ) : null}

      {config.status === 'unavailable' ? (
        <section className="security__unavailable" aria-labelledby="detail-unavailable">
          <h2 id="detail-unavailable">Grants and holders cannot be shown</h2>
          <p>{config.reason}</p>
        </section>
      ) : null}

      <div className="security security--stacked">
        <section className="security__panel" aria-labelledby="grants-heading">
          <div className="security__panel-bar">
            <h2 id="grants-heading">What this role grants</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => openPermission(null)}
              >
                Set a permission
              </button>
            ) : null}
          </div>
          <p className="security__hint">
            The access this role resolves to, page by page and capability by capability. A
            default comes from the built-in matrix; an administrator&rsquo;s rule replaces the
            default for that resource, including one set to None to take access away.
          </p>
          {permNotice && !permOpen ? (
            <p className={`security__notice security__notice--${permNotice.tone}`} role="status">
              {permNotice.message}
            </p>
          ) : null}

          {loading ? (
            <p role="status">Loading permissions…</p>
          ) : grants.length === 0 ? (
            <p>
              This role grants nothing. No default in the built-in matrix names it, and no
              permission rule has been set for it.
            </p>
          ) : (
            <table className="security__table">
              <thead>
                <tr>
                  <th scope="col">Resource</th>
                  <th scope="col">Access</th>
                  <th scope="col">Source</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {grants.map((grant) => (
                  <tr
                    key={grant.resource}
                    data-inactive={grant.level === 'None' ? 'true' : undefined}
                  >
                    <td>{grant.resource}</td>
                    <td>{grant.level}</td>
                    <td>{grant.source}</td>
                    {canManage ? (
                      <td className="security__row-actions">
                        <button
                          type="button"
                          className="security__link-btn"
                          onClick={() => openPermission(grant)}
                          disabled={rowBusy !== null}
                        >
                          Change level
                        </button>
                        {grant.overrideId ? (
                          <button
                            type="button"
                            className="security__link-btn"
                            onClick={() => onWithdrawOverride(grant)}
                            disabled={rowBusy !== null}
                          >
                            {rowBusy === grant.overrideId ? 'Working…' : 'Withdraw override'}
                          </button>
                        ) : null}
                      </td>
                    ) : null}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>

        <section className="security__panel" aria-labelledby="holders-heading">
          <div className="security__panel-bar">
            <h2 id="holders-heading">Who holds this role</h2>
            {canManage ? (
              <button
                type="button"
                className="security__btn"
                onClick={() => {
                  setEmail('');
                  setAssignNotice(null);
                  setAssignOpen(true);
                }}
              >
                Assign to someone
              </button>
            ) : null}
          </div>
          <p className="security__hint">
            Both sources at once: assignments made here, and web roles granted directly in Power
            Pages. A role granted in Power Pages already grants access; adopting it records the
            decision in the audit trail, and revoking it removes the access (AD-089).
          </p>
          {assignNotice && !assignOpen ? (
            <p className={`security__notice security__notice--${assignNotice.tone}`} role="status">
              {assignNotice.message}
            </p>
          ) : null}

          {holdersState.status === 'loading' ? (
            <p role="status">Loading holders…</p>
          ) : holdersState.status === 'unavailable' ? (
            // Distinct from the config-unavailable section above: this hook has its own
            // failure mode (the Custom API, not the mappings/permissions read), and its own
            // reason to show rather than silently rendering an empty holders list.
            <p className="security__notice security__notice--error" role="status">
              {holdersState.reason}
            </p>
          ) : holders.length === 0 ? (
            <p>Nobody holds this role yet.</p>
          ) : (
            <table className="security__table">
              <thead>
                <tr>
                  <th scope="col">Person</th>
                  <th scope="col">Work email</th>
                  <th scope="col">Status</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {holders.map((holder) => {
                  const state = classifyHolder(holder);
                  return (
                    <tr
                      key={holder.email}
                      data-inactive={state.state === 'consistent' && !holder.mappingActive ? 'true' : undefined}
                    >
                      <td>{holder.name ?? '—'}</td>
                      <td>{holder.email || '—'}</td>
                      <td>{state.label}</td>
                      {canManage ? (
                        <td className="security__row-actions">
                          {state.canAdopt ? (
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => onReconcile(holder, 'Adopt')}
                              disabled={rowBusy !== null}
                            >
                              {rowBusy === holder.email ? 'Working…' : 'Adopt'}
                            </button>
                          ) : null}
                          {state.canRevoke ? (
                            <button
                              type="button"
                              className="security__link-btn"
                              onClick={() => onReconcile(holder, 'Revoke')}
                              disabled={rowBusy !== null}
                            >
                              Revoke
                            </button>
                          ) : null}
                        </td>
                      ) : null}
                    </tr>
                  );
                })}
              </tbody>
            </table>
          )}
        </section>
      </div>

      {permOpen ? (
        <PermissionModal
          notice={permNotice}
          roleOptions={roleOptions}
          selectedRole={roleOptions[0].value}
          onRoleChange={() => {}}
          resource={resource}
          onResourceChange={setResource}
          level={level}
          onLevelChange={setLevel}
          busy={permBusy}
          onSubmit={onSetPermission}
          onClose={() => setPermOpen(false)}
        />
      ) : null}

      {assignOpen ? (
        <AssignRoleModal
          title={`Assign ${role.name}`}
          notice={assignNotice}
          email={email}
          onEmailChange={setEmail}
          emailReadOnly={false}
          roleOptions={roleOptions}
          selectedRole={roleOptions[0].value}
          onRoleChange={() => {}}
          replacingRole={null}
          busy={assignBusy}
          submitLabel="Assign role"
          onSubmit={onAssign}
          onClose={() => setAssignOpen(false)}
        />
      ) : null}

      {roleOpen ? (
        <RoleFormModal
          editing={role}
          notice={roleNotice}
          name={roleName}
          onNameChange={setRoleName}
          description={roleDesc}
          onDescriptionChange={setRoleDesc}
          busy={roleBusy}
          onSubmit={onSubmitRole}
          onClose={() => setRoleOpen(false)}
        />
      ) : null}
    </>
  );
}
