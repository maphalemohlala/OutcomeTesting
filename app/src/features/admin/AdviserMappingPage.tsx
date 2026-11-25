import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { PageIntro } from '../../components/layout/PageIntro';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { usePermissions } from '../../app/permissions/permissionContext';
import {
  useAdviserMappings,
  saveAdviserMapping,
  saveAdviserStatus,
  removeAdviserMapping,
  type AdviserMappingRow,
  type ContactOption,
  type KnownContact,
} from './useAdviserMappings';
import { alreadyMappedRefusal, selfMappingRefusal } from './adviserMappingFailure';
import {
  filterAdvisers,
  groupByAdviser,
  isSelfMapped,
  managerChoices,
  managerLoads,
  type AdviserGroup,
} from './adviserMappingRows';
import { Icon } from '../../components/icons/Icon';
import { Avatar, PersonCell } from '../../components/people/Avatar';
import { Al_advisermappingsal_adviserstatus } from '../../generated/models/Al_advisermappingsModel';
import './AdviserMappingPage.css';

/** The adviser statuses an administrator can set, by option value. */
const STATUS_OPTIONS = Object.entries(Al_advisermappingsal_adviserstatus).map(([value, label]) => ({
  value: Number(value),
  label,
}));
const IO_DECIDES = 'Not set (IO decides)';

function statusLabel(status: number | null | undefined): string {
  return STATUS_OPTIONS.find((o) => o.value === status)?.label ?? IO_DECIDES;
}
import { Skeleton } from '../../components/feedback/Skeleton';
import { Pager } from '../../components/navigation/Pager';
import { PAGE_SIZE, usePaged } from '../../components/navigation/paging';

/**
 * Who each adviser's T&C Managers are (Fixes 5, AD-162).
 *
 * This decides who is told a sign-off is waiting and who may sign it off or record its final
 * outcome. An adviser may have several managers, all equal (owner, 2026-11-24), so one being
 * away holds nothing up. Adding or changing a manager moves that adviser's cases with it,
 * including any already waiting for sign-off (AdviserMappingPlugin).
 */
export function AdviserMappingPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useAdviserMappings(reloadKey);
  const { can } = usePermissions();
  const canEdit = can('page.admin.advisers', 'Manage');
  // The system administrators only (client, 2026-11-25); the server checks the same.
  const canSetStatus = can('adviser.status', 'Edit');
  const [savingStatus, setSavingStatus] = useState<string | null>(null);

  async function setStatus(group: AdviserGroup, value: string) {
    const row = group.managers[0];
    if (!row) return;
    setProblem(null);
    setSavingStatus(group.adviserEmail);
    const result = await saveAdviserStatus(row.id, value === '' ? null : Number(value));
    setSavingStatus(null);
    if (!result.ok) {
      setProblem(result.reason);
      return;
    }
    setReloadKey((k) => k + 1);
  }

  // A row to change, an adviser to add a manager to, or a brand-new mapping.
  const [editing, setEditing] = useState<AdviserMappingRow | AdviserGroup | 'new' | null>(null);
  const [removing, setRemoving] = useState<AdviserMappingRow | null>(null);
  const [problem, setProblem] = useState<string | null>(null);
  const [search, setSearch] = useState('');

  const mappings = state.status === 'ready' ? state.mappings : [];
  const advisers = groupByAdviser(mappings);
  const shown = filterAdvisers(mappings, search);
  const paged = usePaged(shown, PAGE_SIZE, search);
  const selfMapped = advisers.filter((g) => g.managers.some(isSelfMapped));
  const managers = managerLoads(mappings);

  return (
    <div className="advisers">
      <PageIntro
        title="Adviser mapping"
        purpose={
          'The T&C Managers who supervise each adviser. An adviser can have more than one, so ' +
          'cover never waits on one person: they are all told when one of the adviser’s cases ' +
          'is waiting for sign-off, and any of them can sign it off or record its final ' +
          'outcome in the portal. An adviser status set here by a system administrator is ' +
          'followed over IO on the adviser’s open cases.'
        }
        actions={
          canEdit && state.status === 'ready' ? (
            <button type="button" className="button-primary" onClick={() => setEditing('new')}>
              <Icon name="plus" />
              Map an adviser
            </button>
          ) : undefined
        }
      />

      {state.status === 'loading' && <Skeleton label="Loading adviser mappings…" rows={6} />}

      {state.status === 'unavailable' && <p className="advisers__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <>
          {mappings.length === 0 ? (
            <p className="advisers__note">
              No adviser is mapped yet. Until an adviser is mapped, nobody is told when their
              cases fall due for sign-off, and nobody can sign those cases off or record their
              final outcome in the portal.
            </p>
          ) : (
            <div className="advisers__layout">
              <div className="advisers__main">
                <FilterBar summary={`${shown.length} of ${advisers.length} advisers`}>
                  <FilterField label="Search" htmlFor="advisers-search" search>
                    <input
                      id="advisers-search"
                      type="search"
                      value={search}
                      onChange={(e) => setSearch(e.target.value)}
                      placeholder="Adviser or manager name"
                    />
                  </FilterField>
                </FilterBar>

                {shown.length === 0 ? (
                  <p className="advisers__note">
                    No mapping matches “{search.trim()}”.{' '}
                    <button type="button" className="advisers__inline" onClick={() => setSearch('')}>
                      Clear the search
                    </button>{' '}
                    to see every adviser.
                  </p>
                ) : (
                  <div className="advisers__card">
                    <table className="advisers__table">
                      <thead>
                        <tr>
                          <th scope="col">Adviser</th>
                          <th scope="col">
                            <span className="advisers__sr">Maps to</span>
                          </th>
                          <th scope="col">T&amp;C Managers</th>
                          <th scope="col">Adviser status</th>
                          {canEdit && (
                            <th scope="col">
                              <span className="advisers__sr">Actions</span>
                            </th>
                          )}
                        </tr>
                      </thead>
                      <tbody>
                        {paged.rows.map((group) => {
                          const adviser = group.adviserName ?? group.adviserEmail;
                          return (
                            <tr key={group.adviserEmail.toLowerCase()}>
                              <td>
                                <PersonCell
                                  name={adviser}
                                  detail={group.adviserName ? group.adviserEmail : null}
                                />
                              </td>
                              <td className="advisers__arrow">
                                <Icon name="arrowRight" />
                              </td>
                              <td>
                                <ul className="advisers__pairs" aria-label={`T&C Managers for ${adviser}`}>
                                  {group.managers.map((row) => (
                                    <li key={row.id} className="advisers__pair">
                                      {row.managerName ? (
                                        <span className="advisers__manager">
                                          <Avatar name={row.managerName} />
                                          <span className="advisers__manager-name">{row.managerName}</span>
                                          {isSelfMapped(row) ? (
                                            <span className="advisers__self">
                                              <Icon name="warning" />
                                              Signs off their own cases
                                            </span>
                                          ) : null}
                                        </span>
                                      ) : (
                                        <span className="advisers__missing">No manager chosen</span>
                                      )}
                                      {canEdit && (
                                        <span className="advisers__pair-actions">
                                          <button
                                            type="button"
                                            className="button-quiet"
                                            onClick={() => setEditing(row)}
                                          >
                                            Change
                                            <span className="advisers__sr">
                                              {' '}
                                              {row.managerName ?? 'manager'} for {adviser}
                                            </span>
                                          </button>
                                          <button
                                            type="button"
                                            className="advisers__remove"
                                            aria-label={`Remove ${row.managerName ?? 'this manager'} from ${adviser}`}
                                            title="Remove this manager"
                                            onClick={() => {
                                              setProblem(null);
                                              setRemoving(row);
                                            }}
                                          >
                                            <Icon name="trash" />
                                          </button>
                                        </span>
                                      )}
                                    </li>
                                  ))}
                                </ul>
                              </td>
                              <td>
                                {canSetStatus ? (
                                  <select
                                    aria-label={`Adviser status for ${adviser}`}
                                    value={group.status == null ? '' : String(group.status)}
                                    disabled={savingStatus === group.adviserEmail}
                                    onChange={(e) => void setStatus(group, e.target.value)}
                                  >
                                    <option value="">{IO_DECIDES}</option>
                                    {STATUS_OPTIONS.map((o) => (
                                      <option key={o.value} value={o.value}>
                                        {o.label}
                                      </option>
                                    ))}
                                  </select>
                                ) : (
                                  <span>{statusLabel(group.status)}</span>
                                )}
                              </td>
                              {canEdit && (
                                <td className="advisers__row-actions">
                                  <button
                                    type="button"
                                    className="button-secondary"
                                    onClick={() => setEditing(group)}
                                  >
                                    <Icon name="plus" />
                                    Add a manager
                                    <span className="advisers__sr"> for {adviser}</span>
                                  </button>
                                </td>
                              )}
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                    <Pager {...paged} label="advisers" />
                  </div>
                )}
              </div>

              <aside className="advisers__rail" aria-label="Mapping summary">
                {selfMapped.length > 0 ? (
                  <div className="advisers__warning" role="note">
                    <Icon name="warning" />
                    <div>
                      <p className="advisers__warning-title">
                        {selfMapped.length === 1
                          ? '1 adviser is mapped to themselves'
                          : `${selfMapped.length} advisers are mapped to themselves`}
                      </p>
                      <p>
                        {listNames(selfMapped.map((g) => g.adviserName ?? g.adviserEmail))} would
                        sign off their own cases.
                      </p>
                    </div>
                  </div>
                ) : null}

                {managers.length > 0 ? (
                  <section className="advisers__managers" aria-labelledby="advisers-managers">
                    <header>
                      <h2 id="advisers-managers">Managers</h2>
                      <span>
                        {managers.length} {managers.length === 1 ? 'person' : 'people'}
                      </span>
                    </header>
                    <ul>
                      {managers.map((m) => (
                        <li key={m.managerId}>
                          <Avatar name={m.name} />
                          <span className="advisers__managers-text">
                            <span className="advisers__managers-name">{m.name}</span>
                            <span className="advisers__managers-covers">{m.advisers.join(', ')}</span>
                          </span>
                          <span className="advisers__managers-count numeral">
                            {m.advisers.length}
                            <span className="advisers__sr">
                              {m.advisers.length === 1 ? ' adviser' : ' advisers'}
                            </span>
                          </span>
                        </li>
                      ))}
                    </ul>
                  </section>
                ) : null}
              </aside>
            </div>
          )}

          {problem && <p className="advisers__problem">{problem}</p>}

          {editing && (
            <MappingForm
              row={editing === 'new' || !('id' in editing) ? null : editing}
              adviser={editing !== 'new' && !('id' in editing) ? editing : null}
              contacts={state.contacts}
              known={state.known}
              mappings={state.mappings}
              onClose={() => setEditing(null)}
              onSaved={() => {
                setEditing(null);
                setReloadKey((k) => k + 1);
              }}
            />
          )}

          {removing && (
            <RemoveMapping
              row={removing}
              others={mappings.filter(
                (m) =>
                  m.id !== removing.id &&
                  m.adviserEmail.trim().toLowerCase() === removing.adviserEmail.trim().toLowerCase(),
              )}
              onClose={() => setRemoving(null)}
              onRemoved={() => {
                setRemoving(null);
                setReloadKey((k) => k + 1);
              }}
              onProblem={(reason) => {
                setRemoving(null);
                setProblem(reason);
              }}
            />
          )}
        </>
      )}
    </div>
  );
}

/** "A", "A and B", "A, B and C". */
function listNames(names: string[]): string {
  return names.length < 2
    ? (names[0] ?? '')
    : `${names.slice(0, -1).join(', ')} and ${names[names.length - 1]}`;
}

function MappingForm({
  row,
  adviser,
  contacts,
  known,
  mappings,
  onClose,
  onSaved,
}: {
  /** The row whose manager is being changed, or null to add one. */
  row: AdviserMappingRow | null;
  /** The adviser to add a manager to, or null to choose an adviser too. */
  adviser: AdviserGroup | null;
  contacts: ContactOption[];
  known: KnownContact[];
  /** What is already mapped, so a duplicate is named here rather than guessed at. */
  mappings: AdviserMappingRow[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [adviserEmail, setAdviserEmail] = useState(row?.adviserEmail ?? adviser?.adviserEmail ?? '');
  const [managerId, setManagerId] = useState(row?.managerId ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setProblem(null);

    // The same manager twice for one adviser. The alternate key is what actually prevents it;
    // this is here so the answer is immediate and names the problem rather than the fault.
    const clash = alreadyMappedRefusal(adviserEmail, managerId, mappings, row?.id ?? null);
    if (clash) {
      setProblem(clash);
      return;
    }

    // Matched by email against everyone known, so an inactive manager on record counts too.
    const self = selfMappingRefusal(adviserEmail, managerId, known);
    if (self) {
      setProblem(self);
      return;
    }

    setSaving(true);
    const result = await saveAdviserMapping(row?.id ?? null, adviserEmail, managerId);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  // A pop-up rather than a panel under the table, for the reason the question library's
  // editors already are: the list behind it stays a list, and a second Change click cannot
  // swap the form's adviser out from under a half-finished edit. Modal brings Escape, the
  // backdrop and the focus move with it, so none of that is re-implemented here.
  return (
    <Modal
      title={row ? 'Change T&C Manager' : adviser ? 'Add a T&C Manager' : 'Map an adviser'}
      onClose={onClose}
    >
      <form className="advisers__form" onSubmit={submit}>
        <label htmlFor="adviser-email">Adviser</label>
        {/*
          Chosen from contacts rather than typed. A mapping is keyed on the adviser's email and
          matched against the case's al_adviseremail exactly, so a typo produced a row that
          looked right in this list and routed nothing.

          Fixed when changing a row or adding a manager to an adviser: choosing somebody else
          here would silently move the mapping to a different adviser rather than add to this one.
          Map the other adviser instead.
        */}
        {row !== null || adviser !== null ? (
          <input id="adviser-email" type="email" value={adviserEmail} readOnly />
        ) : (
          <select
            id="adviser-email"
            value={adviserEmail}
            onChange={(e) => setAdviserEmail(e.target.value)}
          >
            <option value="">Choose…</option>
            {contacts.map((c) => (
              <option key={c.id} value={c.email}>
                {c.name} ({c.email})
              </option>
            ))}
          </select>
        )}
        <p className="advisers__hint">
          The adviser’s email has to match the one on their cases, which the import takes from
          the extract’s AdviserEmail column. An adviser who is not a contact cannot be picked
          here — add the contact first.
        </p>

        <label htmlFor="manager">T&amp;C Manager</label>
        <select id="manager" value={managerId} onChange={(e) => setManagerId(e.target.value)}>
          <option value="">Choose…</option>
          {managerChoices(contacts, known, row?.managerId).map((m) => (
            <option key={m.id} value={m.id}>
              {m.label}
            </option>
          ))}
        </select>
        <p className="advisers__hint">
          Only contacts with a work email are listed. A manager with no email address would be
          mapped but never written to. An adviser can have several managers; each is told about
          their cases and any of them can sign off.
        </p>

        {problem && <p className="advisers__problem">{problem}</p>}

        <div className="advisers__actions">
          <button
            type="button"
            className="advisers__btn advisers__btn--ghost"
            onClick={onClose}
            disabled={saving}
          >
            Cancel
          </button>
          <button
            type="submit"
            className="advisers__btn"
            disabled={saving || adviserEmail.trim() === '' || managerId === ''}
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

/**
 * Confirms removing a mapping (F18).
 *
 * Removal used to be impossible: the only action was Change, and clearing the manager was
 * refused, so an adviser who should no longer be routed kept a stale T&C Manager for good.
 *
 * Confirmed rather than done on the first click, because the row cannot be recovered from
 * this page - and the consequence is spelled out, because "remove" on a screen about
 * permissions-shaped things invites the fear that access is being taken away. It is not.
 */
function RemoveMapping({
  row,
  others,
  onClose,
  onRemoved,
  onProblem,
}: {
  row: AdviserMappingRow;
  /** The adviser's other managers, who keep their cases. */
  others: AdviserMappingRow[];
  onClose: () => void;
  onRemoved: () => void;
  onProblem: (reason: string) => void;
}) {
  const [removing, setRemoving] = useState(false);

  async function confirm() {
    setRemoving(true);
    const result = await removeAdviserMapping(row.id);
    setRemoving(false);

    if (result.ok) {
      onRemoved();
      return;
    }

    onProblem(result.reason);
  }

  return (
    <Modal title="Remove T&C Manager" onClose={onClose}>
      <div className="advisers__form">
        <p>
          <strong>{row.adviserEmail}</strong>
          {row.managerName ? <> is mapped to {row.managerName}.</> : <> has no manager chosen.</>}
        </p>
        {others.length > 0 ? (
          <p className="advisers__hint">
            Removing it stops {row.managerName ?? 'this manager'} being told when a sign-off falls
            due for this adviser, and their cases come off this manager&apos;s list.{' '}
            {listNames(others.map((o) => o.managerName ?? 'Manager on record'))}{' '}
            {others.length === 1 ? 'still covers' : 'still cover'} them and can sign them off.
          </p>
        ) : (
          <p className="advisers__hint">
            Removing it stops {row.managerName ?? 'anyone'} being told when a sign-off falls due
            for this adviser. It is their only manager, so until they are mapped again nobody can
            sign off their cases or record a final outcome, and those cases come off this
            manager&apos;s list.
          </p>
        )}

        <div className="advisers__actions">
          <button
            type="button"
            className="advisers__btn advisers__btn--ghost"
            onClick={onClose}
            disabled={removing}
          >
            Cancel
          </button>
          <button type="button" className="advisers__btn" onClick={confirm} disabled={removing}>
            {removing ? 'Removing…' : 'Remove manager'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
