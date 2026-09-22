import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { ExportMenu } from '../../components/export/ExportMenu';
import { Notice } from '../../components/feedback/Notice';
import { OUTCOMES } from '../../types/domain';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useUserDirectory, type DirectoryUser } from '../../hooks/useUserDirectory';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { setUserActive } from '../../services/commands/users';
import { assignUserRole, setRoleAssignmentActive } from '../../services/commands/permissions';
import { useCaseWorklist } from '../cases/useCaseWorklist';
import { useSecurityConfig } from '../admin/useSecurityConfig';
import { useRoles } from '../admin/useRoles';
import { caseloadByName, type PersonCaseload, type PersonRole } from './peopleDirectory';
import { matchesRole } from './peopleFilters';
import { RoleAssignment } from './RoleAssignmentCell';
import { CreatePersonModal, EditPersonModal } from './PersonModals';
import './PeoplePage.css';
import './PeopleAdmin.css';

const EXPORT_HEADERS = [
  'Name',
  'Work email',
  'Status',
  'Roles',
  'Positions',
  // The REGISTRY's code, the same value the table cell and the search box use. This said
  // 'Code' and carried row.load?.code - the retired per-case column - so an administrator
  // read one number on screen and downloaded another (2026-09-22 review). The label follows
  // the People page's own column heading (D9).
  'Employee code',
  'Cases',
  'Open',
  'Closed',
  ...OUTCOMES,
  'Not yet graded',
  'Oldest open (days)',
];

type Banner = { tone: 'success' | 'error'; message: string } | null;
type ActiveFilter = 'all' | 'active' | 'inactive';

/**
 * A person in the directory, with whatever caseload the cases show for them.
 * `user` is absent for someone named on a case who is not in the directory — an adviser
 * from the intake extract, typically. Those are listed rather than hidden: dropping them
 * would quietly shorten the workload picture the page exists to give.
 */
interface PersonRow {
  key: string;
  name: string;
  email: string;
  active: boolean;
  createdOn: string | null;
  user: DirectoryUser | null;
  load: PersonCaseload | null;
  /** Web roles held, from the role mappings (AD-041). */
  roles: string[];
}

function zeroes(): Record<string, number> {
  return Object.fromEntries(OUTCOMES.map((outcome) => [outcome, 0]));
}

/** Where a person's cases live: their first position, since that route is per-position. */
function drillTo(load: PersonCaseload | null): string | null {
  const role: PersonRole | undefined = load?.roles[0];
  return role ? `/people/${role}/${encodeURIComponent(load!.name)}` : null;
}

export function PeoplePage() {
  const { can, ready } = usePermissions();
  const canManage = ready && can('permission.manage', 'Manage');

  const [reloadKey, setReloadKey] = useState(0);
  const directory = useUserDirectory(reloadKey);
  const cases = useCaseWorklist();
  const security = useSecurityConfig(reloadKey);
  const roleList = useRoles(reloadKey);

  /**
   * The roles this page may filter by and grant: the live Power Pages web roles.
   *
   * Read from the server rather than hard-coded, exactly as Security configuration does.
   * A hard-coded list here carried the retired `al_Role` labels, so the filter matched
   * nobody and every Grant was refused - see peopleFilters for what that looked like.
   */
  const grantable = useMemo(
    () =>
      roleList.status === 'ready'
        ? roleList.roles.filter((role) => role.active).map((role) => role.name)
        : [],
    [roleList],
  );

  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<ActiveFilter>('all');
  const [roleFilter, setRoleFilter] = useState<string>('all');
  const [banner, setBanner] = useState<Banner>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<DirectoryUser | null>(null);
  const [roleBusy, setRoleBusy] = useState<string | null>(null);
  const intent = useIntentKeys();

  const loads = useMemo(
    () => caseloadByName(cases.status === 'ready' ? cases.cases : []),
    [cases],
  );

  /**
   * Each person's roles, keyed on work email.
   *
   * Read from the role mappings rather than the web role associations directly: assigning
   * writes both, so the mapping carries the same facts and is a table the app can already
   * read. Withdrawn assignments are left out — the row is kept for the audit trail, but it
   * no longer describes what someone holds.
   */
  const rolesByEmail = useMemo(() => {
    const byEmail = new Map<string, string[]>();
    if (security.status !== 'ready') return byEmail;

    for (const mapping of security.mappings) {
      if (!mapping.active) continue;
      const key = mapping.email.trim().toLowerCase();
      if (key === '') continue;
      const held = byEmail.get(key) ?? [];
      if (!held.includes(mapping.role)) held.push(mapping.role);
      byEmail.set(key, held);
    }
    return byEmail;
  }, [security]);

  const rows = useMemo<PersonRow[]>(() => {
    const users = directory.status === 'ready' ? directory.users : [];
    const claimed = new Set<string>();

    // The directory is the spine: everyone in it appears, with or without cases.
    const registered = users.map((user) => {
      const key = user.name.trim().toLowerCase();
      claimed.add(key);
      return {
        key: `user:${user.id}`,
        name: user.name,
        email: user.email,
        active: user.active,
        createdOn: user.createdOn,
        user,
        load: loads.get(key) ?? null,
        roles: rolesByEmail.get(user.email.trim().toLowerCase()) ?? [],
      } satisfies PersonRow;
    });

    // Then anyone the cases name who the directory does not hold.
    const unregistered = [...loads.entries()]
      .filter(([key]) => !claimed.has(key))
      .map(([key, load]) => ({
        key: `case:${key}`,
        name: load.name,
        email: '',
        active: true,
        createdOn: null,
        user: null,
        load,
        roles: [],
      }) satisfies PersonRow);

    return [...registered, ...unregistered].sort(
      (a, b) => (b.load?.totalCases ?? 0) - (a.load?.totalCases ?? 0) || a.name.localeCompare(b.name),
    );
  }, [directory, loads, rolesByEmail]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (activeFilter === 'active' && !row.active) return false;
      if (activeFilter === 'inactive' && row.active) return false;
      if (!matchesRole(row.roles, roleFilter)) return false;
      const haystack = `${row.name} ${row.email} ${row.user?.staffCode ?? ''} ${row.roles.join(' ')}`;
      if (term && !haystack.toLowerCase().includes(term)) {
        return false;
      }
      return true;
    });
  }, [rows, search, activeFilter, roleFilter]);

  const isFiltered = search.trim() !== '' || activeFilter !== 'all' || roleFilter !== 'all';
  const unregisteredCount = rows.filter((row) => row.user === null).length;

  function reload() {
    setReloadKey((k) => k + 1);
  }

  async function onToggleActive(user: DirectoryUser) {
    setBanner(null);
    const token = `active:${user.id}`;
    const result = await setUserActive({
      userId: user.id,
      active: !user.active,
      idempotencyKey: intent.keyFor(token),
    });
    if (result.ok) {
      intent.release(token);
      setBanner({
        tone: 'success',
        message: `${user.name} ${user.active ? 'deactivated' : 'reactivated'}.`,
      });
      reload();
    } else {
      setBanner({ tone: 'error', message: messageForFailure(result) });
    }
  }

  async function onGrantRole(email: string, role: string) {
    setBanner(null);
    setRoleBusy(email);
    const token = `grant:${email}:${role}`;
    const result = await assignUserRole({
      userEmail: email,
      // The web role's NAME is the code al_rolecode carries (AD-044); there is no separate
      // business key to look up.
      roleCode: role,
      idempotencyKey: intent.keyFor(token),
    });
    setRoleBusy(null);
    if (result.ok) {
      intent.release(token);
      setBanner({ tone: 'success', message: `${role} granted to ${email}.` });
      reload();
    } else {
      setBanner({ tone: 'error', message: messageForFailure(result) });
    }
  }

  async function onWithdrawRole(mappingId: string, email: string, role: string) {
    setBanner(null);
    setRoleBusy(email);
    const token = `withdraw:${mappingId}`;
    const result = await setRoleAssignmentActive({
      id: mappingId,
      active: false,
      idempotencyKey: intent.keyFor(token),
    });
    setRoleBusy(null);
    if (result.ok) {
      intent.release(token);
      setBanner({ tone: 'success', message: `${role} withdrawn from ${email}.` });
      reload();
    } else {
      setBanner({ tone: 'error', message: messageForFailure(result) });
    }
  }

  return (
    <>
      <PageIntro
        title="People"
        purpose="The people known to the application, their roles and their employee codes. Sourced from Contacts and keyed on work email (AD-010). A role here grants access to this application; changes are enforced server-side and recorded in the audit trail (AD-041)."
        actions={
          <>
            {filtered.length > 0 ? (
              <ExportMenu
                label="Export people"
                stem="outcome-people"
                sheetName="People"
                headers={EXPORT_HEADERS}
                rows={filtered.map((row) => {
                  const outcomes = row.load?.outcomes ?? zeroes();
                  return [
                    row.name,
                    row.email,
                    row.user ? (row.active ? 'Active' : 'Inactive') : 'Not in directory',
                    row.roles.join(', '),
                    (row.load?.roles ?? []).join(', '),
                    row.user?.staffCode ?? '',
                    row.load?.totalCases ?? 0,
                    row.load?.openCases ?? 0,
                    row.load?.closedCases ?? 0,
                    ...OUTCOMES.map((outcome) => outcomes[outcome] ?? 0),
                    row.load?.notGraded ?? 0,
                    row.load?.oldestOpenDays ?? 0,
                  ];
                })}
                caption={`Exports the ${filtered.length} person row${filtered.length === 1 ? '' : 's'} currently listed`}
              />
            ) : null}
            {canManage ? (
              <button
                type="button"
                className="users__btn"
                onClick={() => {
                  setBanner(null);
                  setCreateOpen(true);
                }}
              >
                Add person
              </button>
            ) : null}
          </>
        }
      />

      {banner ? <Notice tone={banner.tone}>{banner.message}</Notice> : null}

      {directory.status === 'loading' || cases.status === 'loading' ? (
        <p role="status">Loading people…</p>
      ) : null}

      {directory.status === 'unavailable' ? (
        <section className="people__unavailable" aria-labelledby="people-unavailable">
          <h2 id="people-unavailable">People cannot be listed</h2>
          <p>{directory.reason}</p>
        </section>
      ) : null}

      {directory.status === 'ready' ? (
        <>
          <p className="people__note">
            The directory is the Contacts in this environment. Caseload is joined by the name
            recorded on the case — adviser, paraplanner and checker come from the intake extract,
            owner is the person the case is allocated to (BR-003, AD-029).
            {canManage ? (
              <>
                {' '}
                Roles are Power Pages web roles; assign and configure them under{' '}
                <Link to="/admin/security">Security configuration</Link>.
              </>
            ) : null}
            {unregisteredCount > 0
              ? ` ${unregisteredCount} name${unregisteredCount === 1 ? '' : 's'} on cases ${unregisteredCount === 1 ? 'is' : 'are'} not in the directory, and cannot be allocated work until added.`
              : ''}
          </p>

          <FilterBar
            summary={`${filtered.length} of ${rows.length} people`}
            onClear={() => {
              setSearch('');
              setActiveFilter('all');
              setRoleFilter('all');
            }}
            clearDisabled={!isFiltered}
          >
            <FilterField label="Search" htmlFor="people-search">
              <input
                id="people-search"
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder="Name, email or code"
              />
            </FilterField>
            <FilterField label="Status" htmlFor="people-active">
              <select
                id="people-active"
                value={activeFilter}
                onChange={(e) => setActiveFilter(e.target.value as ActiveFilter)}
              >
                <option value="all">All</option>
                <option value="active">Active</option>
                <option value="inactive">Inactive</option>
              </select>
            </FilterField>
            <FilterField label="Role" htmlFor="people-role">
              <select
                id="people-role"
                value={roleFilter}
                onChange={(event) => setRoleFilter(event.target.value)}
              >
                <option value="all">All roles</option>
                {grantable.map((role) => (
                  <option key={role} value={role}>
                    {role}
                  </option>
                ))}
              </select>
            </FilterField>
          </FilterBar>

          <div className="people__scroll">
            <table className="people">
              <caption className="visually-hidden">
                People in the directory and named on cases, busiest first
              </caption>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Work email</th>
                  <th scope="col">Role</th>
                  <th scope="col">Employee code</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={4 + (canManage ? 1 : 0)} className="people__empty">
                      No people match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((row) => {
                    const to = drillTo(row.load);
                    return (
                      <tr key={row.key}>
                        <th scope="row">{to ? <Link to={to}>{row.name}</Link> : row.name}</th>
                        <td>{row.email || '—'}</td>
                        <td>
                          <RoleAssignment
                            email={row.email}
                            roles={row.roles}
                            mappings={security.status === 'ready' ? security.mappings : []}
                            grantable={grantable}
                            canManage={canManage}
                            busy={roleBusy === row.email}
                            onGrant={onGrantRole}
                            onWithdraw={onWithdrawRole}
                          />
                        </td>
                        <td>{row.user?.staffCode ?? '—'}</td>
                        {canManage ? (
                          <td className="users__actions">
                            {row.user ? (
                              <>
                                <button
                                  type="button"
                                  className="users__link"
                                  onClick={() => {
                                    setBanner(null);
                                    setEditing(row.user);
                                  }}
                                >
                                  Edit
                                </button>
                                <button
                                  type="button"
                                  className="users__link"
                                  onClick={() => onToggleActive(row.user!)}
                                >
                                  {row.active ? 'Deactivate' : 'Reactivate'}
                                </button>
                              </>
                            ) : (
                              '—'
                            )}
                          </td>
                        ) : null}
                      </tr>
                    );
                  })
                )}
              </tbody>
            </table>
          </div>
        </>
      ) : null}

      {createOpen ? (
        <CreatePersonModal
          onClose={() => setCreateOpen(false)}
          onDone={(message) => {
            setBanner({ tone: 'success', message });
            setCreateOpen(false);
            reload();
          }}
        />
      ) : null}

      {editing ? (
        <EditPersonModal
          user={editing}
          onClose={() => setEditing(null)}
          onDone={(message) => {
            setBanner({ tone: 'success', message });
            setEditing(null);
            reload();
          }}
        />
      ) : null}
    </>
  );
}
