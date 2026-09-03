import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';
import { PageIntro } from '../../components/layout/PageIntro';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { usePermissions } from '../../app/permissions/permissionContext';
import { useUserDirectory, type DirectoryUser } from '../../hooks/useUserDirectory';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { createUser, updateUser, setUserActive } from '../../services/commands/users';
import './UsersPage.css';

type Banner = { tone: 'success' | 'error'; message: string } | null;
type ActiveFilter = 'all' | 'active' | 'inactive';

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '—' : date.toLocaleDateString();
}

export function UsersPage() {
  const { can, ready } = usePermissions();
  const canManage = ready && can('permission.manage', 'Manage');

  const [reloadKey, setReloadKey] = useState(0);
  const directory = useUserDirectory(reloadKey);

  const [search, setSearch] = useState('');
  const [activeFilter, setActiveFilter] = useState<ActiveFilter>('all');
  const [banner, setBanner] = useState<Banner>(null);

  const [createOpen, setCreateOpen] = useState(false);
  const [editing, setEditing] = useState<DirectoryUser | null>(null);
  const intent = useIntentKeys();

  const users = directory.status === 'ready' ? directory.users : [];

  const filtered = useMemo(() => {
    const term = search.trim().toLowerCase();
    return users.filter((user) => {
      if (activeFilter === 'active' && !user.active) return false;
      if (activeFilter === 'inactive' && user.active) return false;
      if (term && !`${user.name} ${user.email}`.toLowerCase().includes(term)) return false;
      return true;
    });
  }, [users, search, activeFilter]);

  const isFiltered = search.trim() !== '' || activeFilter !== 'all';

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
        title="Users"
        purpose="Manage the people known to the application, keyed on work email (AD-010). Changes are enforced server-side and recorded in the audit trail (AD-041)."
      />

      {banner ? <Notice tone={banner.tone}>{banner.message}</Notice> : null}

      {directory.status === 'loading' ? <p role="status">Loading users…</p> : null}

      {directory.status === 'unavailable' ? (
        <section className="users__unavailable" aria-labelledby="users-unavailable">
          <h2 id="users-unavailable">The user list is unavailable</h2>
          <p>{directory.reason}</p>
        </section>
      ) : null}

      {directory.status === 'ready' ? (
        <>
          <div className="users__toolbar">
            <FilterBar
              summary={`${filtered.length} of ${users.length} users`}
              onClear={() => {
                setSearch('');
                setActiveFilter('all');
              }}
              clearDisabled={!isFiltered}
            >
              <FilterField label="Search" htmlFor="users-search">
                <input
                  id="users-search"
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="Name or email"
                />
              </FilterField>
              <FilterField label="Status" htmlFor="users-active">
                <select
                  id="users-active"
                  value={activeFilter}
                  onChange={(e) => setActiveFilter(e.target.value as ActiveFilter)}
                >
                  <option value="all">All</option>
                  <option value="active">Active</option>
                  <option value="inactive">Inactive</option>
                </select>
              </FilterField>
            </FilterBar>

            {canManage ? (
              <button
                type="button"
                className="users__btn"
                onClick={() => {
                  setBanner(null);
                  setCreateOpen(true);
                }}
              >
                Add user
              </button>
            ) : null}
          </div>

          <div className="users__scroll">
            <table className="users">
              <caption className="visually-hidden">Application users</caption>
              <thead>
                <tr>
                  <th scope="col">Name</th>
                  <th scope="col">Work email</th>
                  <th scope="col">Status</th>
                  <th scope="col">Added</th>
                  <th scope="col">Cases</th>
                  {canManage ? <th scope="col">Actions</th> : null}
                </tr>
              </thead>
              <tbody>
                {filtered.length === 0 ? (
                  <tr>
                    <td colSpan={canManage ? 6 : 5} className="users__empty">
                      No users match your current filters.
                    </td>
                  </tr>
                ) : (
                  filtered.map((user) => (
                    <tr key={user.id}>
                      <th scope="row">{user.name}</th>
                      <td>{user.email}</td>
                      <td>
                        <span
                          className={`users__badge users__badge--${user.active ? 'active' : 'inactive'}`}
                        >
                          {user.active ? 'Active' : 'Inactive'}
                        </span>
                      </td>
                      <td>{formatDate(user.createdOn)}</td>
                      <td>
                        <Link
                          className="users__link"
                          to={`/cases?person=${encodeURIComponent(user.name)}`}
                        >
                          View cases
                        </Link>
                      </td>
                      {canManage ? (
                        <td className="users__actions">
                          <button
                            type="button"
                            className="users__link"
                            onClick={() => {
                              setBanner(null);
                              setEditing(user);
                            }}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="users__link"
                            onClick={() => onToggleActive(user)}
                          >
                            {user.active ? 'Deactivate' : 'Reactivate'}
                          </button>
                        </td>
                      ) : null}
                    </tr>
                  ))
                )}
              </tbody>
            </table>
          </div>
        </>
      ) : null}

      {createOpen ? (
        <CreateUserModal
          onClose={() => setCreateOpen(false)}
          onDone={(message) => {
            setBanner({ tone: 'success', message });
            setCreateOpen(false);
            reload();
          }}
        />
      ) : null}

      {editing ? (
        <EditUserModal
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

function CreateUserModal({
  onClose,
  onDone,
}: {
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const [name, setName] = useState('');
  const [email, setEmail] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!name.trim()) {
      setError('Enter the person’s full name.');
      return;
    }
    if (!email.trim()) {
      setError('Enter a work email.');
      return;
    }
    setBusy(true);
    setError(null);
    const result = await createUser({
      fullName: name.trim(),
      workEmail: email.trim(),
      idempotencyKey: intent.keyFor('create'),
    });
    setBusy(false);
    if (result.ok) {
      intent.release('create');
      onDone(`${name.trim()} added to the user registry.`);
    } else {
      setError(messageForFailure(result));
    }
  }

  return (
    <Modal title="Add user" onClose={onClose}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      <form className="users__form" onSubmit={onSubmit}>
        <label className="users__field">
          <span>Full name</span>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </label>
        <label className="users__field">
          <span>Work email</span>
          <input
            type="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="person@ascotlloyd.co.uk"
            autoComplete="off"
          />
        </label>
        <div className="users__form-actions">
          <button
            type="button"
            className="users__btn users__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button type="submit" className="users__btn" disabled={busy}>
            {busy ? 'Adding…' : 'Add user'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

function EditUserModal({
  user,
  onClose,
  onDone,
}: {
  user: DirectoryUser;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const [name, setName] = useState(user.name);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (!name.trim()) {
      setError('Enter the person’s full name.');
      return;
    }
    setBusy(true);
    setError(null);
    const result = await updateUser({
      userId: user.id,
      fullName: name.trim(),
      expectedRowVersion: user.rowVersion,
      idempotencyKey: intent.keyFor(user.id),
    });
    setBusy(false);
    if (result.ok) {
      intent.release(user.id);
      onDone(`${name.trim()} updated.`);
    } else {
      setError(messageForFailure(result));
    }
  }

  return (
    <Modal title="Edit user" onClose={onClose}>
      {error ? <Notice tone="error">{error}</Notice> : null}
      <form className="users__form" onSubmit={onSubmit}>
        <label className="users__field">
          <span>Full name</span>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} autoComplete="off" />
        </label>
        <label className="users__field">
          <span>Work email</span>
          <input type="email" value={user.email} readOnly disabled />
          <small className="users__hint">Work email is the stable identifier and cannot be changed here.</small>
        </label>
        <div className="users__form-actions">
          <button
            type="button"
            className="users__btn users__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button type="submit" className="users__btn" disabled={busy}>
            {busy ? 'Saving…' : 'Save changes'}
          </button>
        </div>
      </form>
    </Modal>
  );
}
