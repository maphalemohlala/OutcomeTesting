import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
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
import { executeCommand } from '../../services/commands/commandClient';
import { logTechnical, messageForFailure } from '../../services/errors';
import { ACCESS_LEVELS, RESOURCE_KEYS } from '../../types/permissions';
import {
  AssignRoleModal,
  PermissionModal,
  RoleFormModal,
  type Notice,
  type RoleOption,
} from './SecurityModals';
import {
  classifyHolder,
  isRetiredRole,
  roleDetailPath,
  sameRoleCode,
  type RoleHolderRecord,
} from './roleDetail';
import { Icon } from '../../components/icons/Icon';
import { PersonCell } from '../../components/people/Avatar';
import { Modal } from '../../components/feedback/Modal';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import { holdingSummary, matrixColumns, matrixRows, roleDisplayName, type MatrixRow } from './securityMatrix';
import './SecurityConfigPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';
import { Pager } from '../../components/navigation/Pager';
import { PAGE_SIZE, usePaged } from '../../components/navigation/paging';

/** The matrix opens short, so the role columns and the legend are visible without scrolling. */

/** One role whose holders include a state `classifyHolder` does not call `consistent` (AD-089). */
interface UnreconciledRole {
  code: string;
  name: string;
}

/** Splits the `builtin:Label` / `custom:CODE` select value into the two command arguments. */
function roleArgs(selected: string): { appRole?: string; roleCode?: string; label: string } {
  const value = selected.slice(selected.indexOf(':') + 1);
  return selected.startsWith('custom:')
    ? { roleCode: value, label: value }
    : { appRole: value, label: value };
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
  const intent = useIntentKeys();

  // The discrepancy count an administrator sees first (AD-089): one al_GetRoleHolders call
  // per active role, tolerating an individual failure rather than losing the whole count.
  // Fan-out warning (see task brief): acceptable at this environment's eight business roles;
  // past roughly twenty, move this server-side into a dedicated read instead of looping
  // harder here.
  const [unreconciled, setUnreconciled] = useState<UnreconciledRole[]>([]);

  useEffect(() => {
    if (roles.status !== 'ready') return;
    let cancelled = false;
    const activeRoles = roles.roles.filter((role) => role.active);

    Promise.all(
      activeRoles.map(async (role): Promise<UnreconciledRole | null> => {
        const result = await executeCommand<{ Holders: string }>('al_GetRoleHolders', {
          RoleCode: role.code,
        });
        if (!result.ok) {
          logTechnical('role reconciliation count', result.message);
          return null;
        }
        try {
          const holders = JSON.parse(result.data.Holders) as RoleHolderRecord[];
          const hasDiscrepancy = holders.some((holder) => classifyHolder(holder).state !== 'consistent');
          return hasDiscrepancy ? { code: role.code, name: role.name } : null;
        } catch (error) {
          logTechnical('role reconciliation count parse', error);
          return null;
        }
      }),
    ).then((results) => {
      if (cancelled) return;
      setUnreconciled(results.filter((entry): entry is UnreconciledRole => entry !== null));
    });

    return () => {
      cancelled = true;
    };
  }, [roles, reloadKey]);

  const [roleName, setRoleName] = useState('');
  const [roleDesc, setRoleDesc] = useState('');
  const [roleBusy, setRoleBusy] = useState(false);
  const [roleNotice, setRoleNotice] = useState<Notice>(null);
  const [roleOpen, setRoleOpen] = useState(false);
  const [editingRole, setEditingRole] = useState<RoleRow | null>(null);

  const [email, setEmail] = useState('');
  const [assignRole, setAssignRole] = useState<string>('');
  const [assignBusy, setAssignBusy] = useState(false);
  const [assignNotice, setAssignNotice] = useState<Notice>(null);
  const [assignOpen, setAssignOpen] = useState(false);
  /** Set when the assign form is changing an existing assignment, so the old one is withdrawn. */
  const [replacing, setReplacing] = useState<RoleMappingRow | null>(null);

  const [permRole, setPermRole] = useState<string>('');
  const [resource, setResource] = useState<string>(RESOURCE_KEYS[0]);
  const [level, setLevel] = useState<string>(ACCESS_LEVELS[1]);
  const [permBusy, setPermBusy] = useState(false);
  const [permNotice, setPermNotice] = useState<Notice>(null);
  const [permOpen, setPermOpen] = useState(false);

  const [rowBusy, setRowBusy] = useState<string | null>(null);
  const [rowNotice, setRowNotice] = useState<Notice>(null);

  // Every role is a Power Pages web role, and its name is the code that assignments and
  // permission rules match on (al_rolecode, AD-044). The al_approle picklist is no longer
  // offered — the server still reads it, so anything already assigned by it keeps working,
  // but new configuration is written against the web roles the portal actually enforces.
  // A retired role's withdrawn rules and assignments cannot be restored (the server refuses),
  // so their rows offer no Restore.
  const knownRoles = roles.status === 'ready' ? roles.roles : [];
  const roleOptions: RoleOption[] = roles.status === 'ready'
    ? roles.roles
        .filter((role) => role.active)
        .map((role) => ({ value: `custom:${role.code}`, label: role.name, custom: true }))
    : [];

  // The role list is not known at first render, so the selections fall back to the first
  // option rather than being synced into state by an effect — deriving avoids the cascading
  // render that syncing causes, and keeps an explicit choice once one is made.
  const firstRole = roleOptions.length > 0 ? roleOptions[0].value : '';
  const selectedAssignRole = assignRole || firstRole;
  const selectedPermRole = permRole || firstRole;

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
      setAssignNotice({ tone: 'error', message: 'Select the person to assign this role to.' });
      return;
    }
    setAssignBusy(true);
    setAssignNotice(null);

    const { appRole, roleCode, label } = roleArgs(selectedAssignRole);
    const token = `assign:${email.trim().toLowerCase()}:${label}`;
    const result = await assignUserRole({
      userEmail: email.trim(),
      appRole,
      roleCode,
      idempotencyKey: intent.keyFor(token),
    });

    if (!result.ok) {
      setAssignBusy(false);
      setAssignNotice({ tone: 'error', message: messageForFailure(result) });
      return;
    }
    intent.release(token);

    // Grant first, then withdraw: a failure here leaves the person with both roles rather
    // than none. The mapping's business code embeds the role, so a change is two writes.
    let message = `${label} assigned to ${email.trim()}.`;
    let tone: 'ok' | 'error' = 'ok';
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
        message = `${label} assigned, but the previous ${replacing.role} assignment could not be withdrawn: ${messageForFailure(withdrawn)}`;
        tone = 'error';
      }
    }

    setAssignBusy(false);
    setAssignNotice({ tone, message });
    setEmail('');
    setReplacing(null);
    setAssignOpen(false);
    reloadConfig();
  }

  function openPermission(existing: PagePermissionRow | null) {
    if (existing) {
      // Every role now enforces by code, so an existing rule reopens as a code selection
      // whether it was written against a web role or an older custom role. A rule still
      // carrying an al_approle label has no matching option and falls back to the first,
      // which is honest: it cannot be re-saved as a picklist role from here any more.
      setPermRole(`custom:${existing.role}`);
      setResource(existing.resource);
      setLevel(existing.level);
    } else {
      setPermRole('');
    }
    setPermNotice(null);
    setPermOpen(true);
  }

  async function onSetPermission(event: React.FormEvent) {
    event.preventDefault();
    setPermBusy(true);
    setPermNotice(null);

    const { appRole, roleCode, label } = roleArgs(selectedPermRole);
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
      setPermNotice({ tone: 'error', message: messageForFailure(result) });
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
    // The values are in the token: a retry of the SAME save replays, an edited one must not.
    const token = `role:${editing?.id ?? 'new'}:${roleName.trim()}:${roleDesc.trim()}`;
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
      setRoleNotice({ tone: 'error', message: messageForFailure(result) });
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
      setRowNotice({ tone: 'error', message: messageForFailure(result) });
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
          ? `Rule withdrawn; ${row.role} no longer has access to ${row.resource}.`
          : `Rule restored for ${row.role} on ${row.resource}.`,
      });
      reloadConfig();
    } else {
      setRowNotice({ tone: 'error', message: messageForFailure(result) });
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
      setRowNotice({ tone: 'error', message: messageForFailure(result) });
    }
  }

  const directory = useUserDirectory();
  const nameByEmail = new Map(
    (directory.status === 'ready' ? directory.users : []).map((u) => [u.email.toLowerCase(), u.name]),
  );
  const personName = (email: string) => nameByEmail.get(email.trim().toLowerCase()) ?? email;

  const [query, setQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'all' | 'active' | 'withdrawn'>('all');
  /** Null until someone picks; until then only the roles somebody holds now are shown. */
  const [pickedHidden, setHiddenRoles] = useState<string[] | null>(null);
  /** A held cell asks first: in a dense matrix a withdrawal would otherwise be one stray click. */
  const [heldChoice, setHeldChoice] = useState<RoleMappingRow | null>(null);

  const allColumns = state.status === 'ready' ? matrixColumns(knownRoles, state.mappings) : [];
  const hiddenRoles =
    pickedHidden ??
    (state.status === 'ready'
      ? allColumns
          .filter((c) => !state.mappings.some((m) => m.active && sameRoleCode(m.role, c.code)))
          .map((c) => c.code)
      : []);
  const columns = allColumns.filter((c) => !hiddenRoles.includes(c.code));
  const needle = query.trim().toLowerCase();
  const rows =
    state.status === 'ready'
      ? matrixRows(state.mappings, columns)
          .filter((row) =>
            statusFilter === 'active' ? row.active > 0 : statusFilter === 'withdrawn' ? row.withdrawn > 0 : true,
          )
          .filter(
            (row) =>
              !needle ||
              row.email.toLowerCase().includes(needle) ||
              personName(row.email).toLowerCase().includes(needle),
          )
          .sort((a, b) => personName(a.email).localeCompare(personName(b.email)))
      : [];
  const peoplePage = usePaged(rows, PAGE_SIZE, `${needle}|${statusFilter}`);
  const shownRows = peoplePage.rows;
  const permissionsPage = usePaged(state.status === 'ready' ? state.permissions : []);
  const rolesPage = usePaged(knownRoles);

  function openAssignFor(email: string, roleCode: string) {
    openAssign(null);
    setEmail(email);
    setAssignRole(`custom:${roleCode}`);
  }

  const notices = [
    assignOpen ? null : assignNotice,
    permOpen ? null : permNotice,
    roleOpen ? null : roleNotice,
    rowNotice,
  ];

  function cellButton(row: MatrixRow, cell: MatrixRow['cells'][number]) {
    const who = personName(row.email);
    const role = cell.column.label;
    if (cell.state === 'held' && cell.mapping) {
      const mapping = cell.mapping;
      return (
        <button
          type="button"
          className="security__toggle"
          data-state="held"
          aria-pressed="true"
          aria-label={`${who} holds ${role}. Withdraw or change it`}
          disabled={!canManage || rowBusy !== null}
          onClick={() => setHeldChoice(mapping)}
        >
          <Icon name="check" />
        </button>
      );
    }
    if (cell.state === 'withdrawn' && cell.mapping) {
      const mapping = cell.mapping;
      const retired = isRetiredRole(mapping.role, knownRoles);
      return (
        <button
          type="button"
          className="security__toggle"
          data-state="withdrawn"
          aria-pressed="false"
          aria-label={
            retired
              ? `${role} withdrawn from ${who}. Role retired, so it cannot be restored`
              : `Restore ${role} for ${who}`
          }
          disabled={!canManage || retired || rowBusy !== null}
          onClick={() => onSetAssignmentActive(mapping)}
        />
      );
    }
    return (
      <button
        type="button"
        className="security__toggle"
        data-state="none"
        aria-pressed="false"
        aria-label={`Assign ${role} to ${who}`}
        disabled={!canManage || cell.column.retired || rowBusy !== null}
        onClick={() => openAssignFor(row.email, cell.column.code)}
      />
    );
  }

  return (
    <>
      <PageIntro
        title="Security configuration"
        purpose="Who holds each application role, and what each role can see and do. Changes are enforced server-side and recorded in the audit trail."
        actions={
          canManage ? (
            <>
              <button type="button" className="button-secondary" onClick={() => openRoleForm(null)}>
                Create role
              </button>
              <button type="button" className="button-secondary" onClick={() => openPermission(null)}>
                Set a permission
              </button>
              <button type="button" className="button-primary" onClick={() => openAssign(null)}>
                <Icon name="plus" />
                Assign a role
              </button>
            </>
          ) : null
        }
      />

      {unreconciled.length > 0 ? (
        <p className="security__notice security__notice--error" role="status">
          {unreconciled.length === 1
            ? '1 role has an assignment that does not match Power Pages: '
            : `${unreconciled.length} roles have assignments that do not match Power Pages: `}
          {unreconciled.map((entry, index) => (
            <span key={entry.code}>
              {index > 0 ? ', ' : ''}
              <Link to={roleDetailPath(entry.code)}>{entry.name}</Link>
            </span>
          ))}
        </p>
      ) : null}
      {notices.map((notice, index) =>
        notice ? (
          <p key={index} className={`security__notice security__notice--${notice.tone}`} role="status">
            {notice.message}
          </p>
        ) : null,
      )}

      {heldChoice ? (
        <Modal
          title={`${roleDisplayName(heldChoice.role)} for ${personName(heldChoice.email)}`}
          onClose={() => setHeldChoice(null)}
        >
          <p className="security__hint">
            Withdrawing keeps the record for the audit trail, and it can be restored later. Changing
            grants the new role first, then withdraws this one.
          </p>
          <div className="security__form-actions">
            <button type="button" className="button-secondary" onClick={() => setHeldChoice(null)}>
              Cancel
            </button>
            <button
              type="button"
              className="button-secondary"
              onClick={() => {
                const mapping = heldChoice;
                setHeldChoice(null);
                openAssign(mapping);
              }}
            >
              Change role
            </button>
            <button
              type="button"
              className="button-primary security__danger"
              onClick={() => {
                const mapping = heldChoice;
                setHeldChoice(null);
                void onSetAssignmentActive(mapping);
              }}
            >
              Withdraw
            </button>
          </div>
        </Modal>
      ) : null}

      {assignOpen ? (
        <AssignRoleModal
          title={replacing ? `Change role for ${replacing.email}` : 'Assign a role'}
          notice={assignNotice}
          email={email}
          onEmailChange={setEmail}
          emailReadOnly={replacing !== null}
          roleOptions={roleOptions}
          selectedRole={selectedAssignRole}
          onRoleChange={setAssignRole}
          replacingRole={replacing ? replacing.role : null}
          busy={assignBusy}
          submitLabel={replacing ? 'Change role' : 'Assign role'}
          onSubmit={onAssign}
          onClose={() => {
            setAssignOpen(false);
            setReplacing(null);
          }}
        />
      ) : null}

      {permOpen ? (
        <PermissionModal
          notice={permNotice}
          roleOptions={roleOptions}
          selectedRole={selectedPermRole}
          onRoleChange={setPermRole}
          resource={resource}
          onResourceChange={setResource}
          level={level}
          onLevelChange={setLevel}
          busy={permBusy}
          onSubmit={onSetPermission}
          onClose={() => setPermOpen(false)}
        />
      ) : null}

      {roleOpen ? (
        <RoleFormModal
          editing={editingRole}
          notice={roleNotice}
          name={roleName}
          onNameChange={setRoleName}
          description={roleDesc}
          onDescriptionChange={setRoleDesc}
          busy={roleBusy}
          onSubmit={onSubmitRole}
          onClose={() => {
            setRoleOpen(false);
            setEditingRole(null);
          }}
        />
      ) : null}

      {state.status === 'loading' ? <Skeleton label="Loading security configuration…" rows={6} /> : null}

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
                state.status === 'loading' ? (
                  <Skeleton label="Loading role assignments…" rows={6} />
                ) : (
                  <p role="status">Role assignments cannot be shown.</p>
                )
              ) : state.mappings.length === 0 ? (
                <p>
                  No roles assigned yet. Everyone has full access until the first role is assigned.
                </p>
              ) : (
                <>
                  <div className="security__filters">
                    <FilterBar
                      onClear={
                        query || statusFilter !== 'all' || pickedHidden
                          ? () => {
                              setQuery('');
                              setStatusFilter('all');
                              setHiddenRoles(null);
                            }
                          : undefined
                      }
                    >
                      <FilterField label="Search" htmlFor="security-search" search>
                        <input
                          id="security-search"
                          type="search"
                          placeholder="Name or work email"
                          value={query}
                          onChange={(event) => setQuery(event.target.value)}
                        />
                      </FilterField>
                      <FilterField label="Status" htmlFor="security-status">
                        <select
                          id="security-status"
                          value={statusFilter}
                          onChange={(event) => setStatusFilter(event.target.value as typeof statusFilter)}
                        >
                          <option value="all">Active and withdrawn</option>
                          <option value="active">Holds an active role</option>
                          <option value="withdrawn">Has a withdrawn role</option>
                        </select>
                      </FilterField>
                      <details className="security__roles-pick">
                        <summary>
                          <span className="security__pill-label">Roles</span>
                          {columns.length} of {allColumns.length} shown
                          <Icon name="chevronDown" />
                        </summary>
                        <fieldset>
                          <legend className="visually-hidden">Role columns shown</legend>
                          {allColumns.map((column) => (
                            <label key={column.code}>
                              <input
                                type="checkbox"
                                checked={!hiddenRoles.includes(column.code)}
                                onChange={(event) =>
                                  setHiddenRoles(
                                    event.target.checked
                                      ? hiddenRoles.filter((code) => code !== column.code)
                                      : [...hiddenRoles, column.code],
                                  )
                                }
                              />
                              {column.label}
                            </label>
                          ))}
                        </fieldset>
                      </details>
                    </FilterBar>
                    <p className="security__people-hint">
                      Only people on the <Link to="/admin/people">People</Link> page can be given a role
                    </p>
                  </div>
                  <div className="security__card">
                    <div className="security__scroll">
                      <table className="security__matrix">
                        <thead>
                          <tr>
                            <th scope="col">Person</th>
                            {columns.map((column) => (
                              <th key={column.code} scope="col" data-privileged={column.privileged || undefined}>
                                {column.label}
                                {column.retired ? <span className="security__retired"> (retired)</span> : null}
                              </th>
                            ))}
                            <th scope="col">
                              <span className="visually-hidden">Roles held</span>
                            </th>
                          </tr>
                        </thead>
                        <tbody>
                          {shownRows.length === 0 ? (
                            <tr>
                              <td colSpan={columns.length + 2} className="security__empty">
                                Nobody matches these filters.
                              </td>
                            </tr>
                          ) : (
                            shownRows.map((row) => (
                              <tr key={row.email.toLowerCase()}>
                                <th scope="row">
                                  <PersonCell
                                    name={personName(row.email)}
                                    detail={personName(row.email) === row.email ? null : row.email}
                                  />
                                </th>
                                {row.cells.map((cell) => (
                                  <td
                                    key={cell.column.code}
                                    className="security__cell"
                                    data-privileged={cell.column.privileged || undefined}
                                  >
                                    {cellButton(row, cell)}
                                  </td>
                                ))}
                                <td className="security__held">{holdingSummary(row)}</td>
                              </tr>
                            ))
                          )}
                        </tbody>
                      </table>
                    </div>
                    <Pager {...peoplePage} label="people" />
                    <div className="security__legend">
                      <ul>
                        <li>
                          <span className="security__toggle" data-state="held" aria-hidden="true">
                            <Icon name="check" />
                          </span>
                          Holds the role. Select to withdraw.
                        </li>
                        <li>
                          <span className="security__toggle" data-state="withdrawn" aria-hidden="true" />
                          Withdrawn. Select to restore.
                        </li>
                        <li>
                          <span className="security__toggle" data-state="none" aria-hidden="true" />
                          Not held. Select to assign.
                        </li>
                      </ul>
                    </div>
                  </div>
                </>
              ),
          },
          {
            id: 'permissions',
            label: 'Permission rules',
            count: state.status === 'ready' ? state.permissions.length : undefined,
            render: () =>
              state.status !== 'ready' ? (
                state.status === 'loading' ? (
                  <Skeleton label="Loading permission rules…" rows={6} />
                ) : (
                  <p role="status">Permission rules cannot be shown.</p>
                )
              ) : state.permissions.length === 0 ? (
                <p>No rules stored. The built-in role defaults apply until the first rule is set.</p>
              ) : (
                <>
                <div className="security__card security__scroll">
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
                    {permissionsPage.rows.map((p) => (
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
                            {p.active || !isRetiredRole(p.role, knownRoles) ? (
                              <button
                                type="button"
                                className="security__link-btn"
                                onClick={() => onSetRuleActive(p)}
                                disabled={rowBusy !== null}
                              >
                                {rowBusy === p.id ? 'Working…' : p.active ? 'Withdraw' : 'Restore'}
                              </button>
                            ) : (
                              <span>Role retired</span>
                            )}
                          </td>
                        ) : null}
                      </tr>
                    ))}
                  </tbody>
                </table>
                </div>
                <Pager {...permissionsPage} label="rules" />
                </>
              ),
          },
          {
            id: 'roles',
            label: 'Role registry',
            count: roles.status === 'ready' ? roles.roles.length : undefined,
            render: () =>
              roles.status === 'loading' ? (
                <Skeleton label="Loading roles…" />
              ) : roles.status === 'unavailable' ? (
                <p>{roles.reason}</p>
              ) : roles.roles.length === 0 ? (
                <p>No roles have been created yet.</p>
              ) : (
                <>
                <div className="security__card security__scroll">
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
                    {rolesPage.rows.map((r) => (
                      <tr key={r.id} data-inactive={r.active ? undefined : 'true'}>
                        <td>
                          <Link to={roleDetailPath(r.code)}>{r.name}</Link>
                        </td>
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
                </div>
                <Pager {...rolesPage} label="roles" />
                </>
              ),
          },
        ]}
      />
    </>
  );
}
