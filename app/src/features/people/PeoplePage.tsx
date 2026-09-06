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
import { useCaseWorklist } from '../cases/useCaseWorklist';
import { caseloadByName, type PersonCaseload, type PersonRole } from './peopleDirectory';
import { CreatePersonModal, EditPersonModal } from './PersonModals';
import './PeoplePage.css';
import './PeopleAdmin.css';

const EXPORT_HEADERS = [
  'Name',
  'Work email',
  'Status',
  'Positions',
  'Code',
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
}

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString();
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

  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<ActiveFilter>('all');
  const [banner, setBanner] = useState<Banner>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<DirectoryUser | null>(null);
  const intent = useIntentKeys();

  const loads = useMemo(
    () => caseloadByName(cases.status === 'ready' ? cases.cases : []),
    [cases],
  );

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
      }) satisfies PersonRow);

    return [...registered, ...unregistered].sort(
      (a, b) => (b.load?.totalCases ?? 0) - (a.load?.totalCases ?? 0) || a.name.localeCompare(b.name),
    );
  }, [directory, loads]);

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return rows.filter((row) => {
      if (activeFilter === 'active' && !row.active) return false;
      if (activeFilter === 'inactive' && row.active) return false;
      if (term && !`${row.name} ${row.email} ${row.load?.code ?? ''}`.toLowerCase().includes(term)) {
        return false;
      }
      return true;
    });
  }, [rows, search, activeFilter]);

  const isFiltered = search.trim() !== '' || activeFilter !== 'all';
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

  return (
    <>
      <PageIntro
        title="People"
        purpose="The people known to the application and the work they carry. Sourced from Contacts and keyed on work email (AD-010); changes are enforced server-side and recorded in the audit trail (AD-041)."
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
                    (row.load?.roles ?? []).join(', '),
                    row.load?.code ?? '',
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
            {unregisteredCount > 0
              ? ` ${unregisteredCount} name${unregisteredCount === 1 ? '' : 's'} on cases ${unregisteredCount === 1 ? 'is' : 'are'} not in the directory, and cannot be allocated work until added.`
              : ''}
          </p>

          <FilterBar
            summary={`${filtered.length} of ${rows.length} people`}
            onClear={() => {
              setSearch('');
              setActiveFilter('all');
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
                  <th scope="col">Status</th>
                  <th scope="col">Positions</th>
                  <th scope="col" className="people__numeric">
                    Cases
                  </th>
                  <th scope="col" className="people__numeric">
                    Open
                  </th>
                  {OUTCOMES.map((outcome) => (
                    <th key={outcome} scope="col" className="people__numeric">
                      {outcome}
                    </th>
                  ))}
                  <th scope="col" className="people__numeric">
                    Not yet graded
                  </th>
                  <th scope="col">Added</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={8 + OUTCOMES.length + (canManage ? 1 : 0)} className="people__empty">
                      No people match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((row) => {
                    const outcomes = row.load?.outcomes ?? zeroes();
                    const to = drillTo(row.load);
                    return (
                      <tr key={row.key}>
                        <th scope="row">{to ? <Link to={to}>{row.name}</Link> : row.name}</th>
                        <td>{row.email || '—'}</td>
                        <td>
                          {row.user ? (
                            <span
                              className={`users__badge users__badge--${row.active ? 'active' : 'inactive'}`}
                            >
                              {row.active ? 'Active' : 'Inactive'}
                            </span>
                          ) : (
                            <span className="users__badge users__badge--inactive">
                              Not in directory
                            </span>
                          )}
                        </td>
                        <td>{(row.load?.roles ?? []).join(', ') || '—'}</td>
                        <td className="people__numeric">{row.load?.totalCases ?? 0}</td>
                        <td className="people__numeric">{row.load?.openCases ?? 0}</td>
                        {OUTCOMES.map((outcome) => (
                          <td key={outcome} className="people__numeric">
                            {outcomes[outcome] ?? 0}
                          </td>
                        ))}
                        <td className="people__numeric">{row.load?.notGraded ?? 0}</td>
                        <td>{formatDate(row.createdOn)}</td>
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
