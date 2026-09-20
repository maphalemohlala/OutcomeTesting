import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import {
  useAdviserMappings,
  saveAdviserMapping,
  removeAdviserMapping,
  type AdviserMappingRow,
  type ContactOption,
} from './useAdviserMappings';
import { alreadyMappedRefusal } from './adviserMappingFailure';
import './AdviserMappingPage.css';

/**
 * Who each adviser's T&C Manager is (Fixes 5, AD-162).
 *
 * The page says plainly, in its own intro, that this is routing and not access. Every T&C
 * Manager can already read every case and sign off any case; what this decides is who is
 * TOLD a sign-off is waiting. An administrator who believed otherwise would use this screen
 * expecting it to restrict something, and it never will.
 */
export function AdviserMappingPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useAdviserMappings(reloadKey);
  const { can } = usePermissions();
  const canEdit = can('page.admin.advisers', 'Manage');

  const [editing, setEditing] = useState<AdviserMappingRow | 'new' | null>(null);
  const [removing, setRemoving] = useState<AdviserMappingRow | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  return (
    <div className="advisers">
      <PageIntro
        title="Adviser mapping"
        purpose={
          'Who is told when one of an adviser’s cases is waiting for sign-off. ' +
          'This routes notifications only — it does not change what anyone can see or do.'
        }
      />

      {state.status === 'loading' && <p className="advisers__note">Loading…</p>}

      {state.status === 'unavailable' && <p className="advisers__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <>
          {canEdit && (
            <button
              type="button"
              className="advisers__btn advisers__add"
              onClick={() => setEditing('new')}
            >
              Map an adviser
            </button>
          )}

          {state.mappings.length === 0 ? (
            <p className="advisers__note">
              No adviser is mapped yet, so nobody is told when a sign-off falls due. The
              sign-off itself still works — any T&amp;C Manager can perform one.
            </p>
          ) : (
            <table className="advisers__table">
              <thead>
                <tr>
                  <th scope="col">Adviser</th>
                  <th scope="col">T&amp;C Manager</th>
                  {canEdit && <th scope="col">
                    <span className="advisers__sr">Actions</span>
                  </th>}
                </tr>
              </thead>
              <tbody>
                {state.mappings.map((row) => (
                  <tr key={row.id}>
                    <td>{row.adviserEmail}</td>
                    <td>
                      {row.managerName ?? (
                        <span className="advisers__missing">No manager chosen</span>
                      )}
                    </td>
                    {canEdit && (
                      <td>
                        <button
                          type="button"
                          className="advisers__btn advisers__btn--ghost"
                          onClick={() => setEditing(row)}
                        >
                          Change
                        </button>
                        <button
                          type="button"
                          className="advisers__btn advisers__btn--ghost"
                          onClick={() => {
                            setProblem(null);
                            setRemoving(row);
                          }}
                        >
                          Remove
                          <span className="advisers__sr"> the mapping for {row.adviserEmail}</span>
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {problem && <p className="advisers__problem">{problem}</p>}

          {editing && (
            <MappingForm
              row={editing === 'new' ? null : editing}
              contacts={state.contacts}
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

function MappingForm({
  row,
  contacts,
  mappings,
  onClose,
  onSaved,
}: {
  row: AdviserMappingRow | null;
  contacts: ContactOption[];
  /** What is already mapped, so a duplicate is named here rather than guessed at. */
  mappings: AdviserMappingRow[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [adviserEmail, setAdviserEmail] = useState(row?.adviserEmail ?? '');
  const [managerId, setManagerId] = useState(row?.managerId ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setProblem(null);

    // Only when adding. Changing a row keeps its own adviser, so it always matches itself.
    // The alternate key is what actually prevents a second row for one adviser; this is
    // here so the answer is immediate and names the adviser rather than the fault.
    if (row === null) {
      const clash = alreadyMappedRefusal(adviserEmail, mappings);
      if (clash) {
        setProblem(clash);
        return;
      }
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
    <Modal title={row ? 'Change mapping' : 'Map an adviser'} onClose={onClose}>
      <form className="advisers__form" onSubmit={submit}>
        <label htmlFor="adviser-email">Adviser</label>
        {/*
          Chosen from contacts rather than typed. A mapping is keyed on the adviser's email and
          matched against the case's al_adviseremail exactly, so a typo produced a row that
          looked right in this list and routed nothing.

          Fixed when changing an existing row: the email IS the alternate key, so choosing
          somebody else here would silently move the mapping to a different adviser rather than
          correct this one. Retire the row and map the other adviser instead.
        */}
        {row !== null ? (
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
          {contacts.map((m) => (
            <option key={m.id} value={m.id}>
              {m.name} ({m.email})
            </option>
          ))}
        </select>
        <p className="advisers__hint">
          Only contacts with a work email are listed. A manager with no email address would be
          mapped but never written to.
        </p>

        {problem && <p className="advisers__problem">{problem}</p>}

        <div className="advisers__actions">
          <button
            type="submit"
            className="advisers__btn"
            disabled={saving || adviserEmail.trim() === '' || managerId === ''}
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
          <button
            type="button"
            className="advisers__btn advisers__btn--ghost"
            onClick={onClose}
            disabled={saving}
          >
            Cancel
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
  onClose,
  onRemoved,
  onProblem,
}: {
  row: AdviserMappingRow;
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
    <Modal title="Remove mapping" onClose={onClose}>
      <div className="advisers__form">
        <p>
          <strong>{row.adviserEmail}</strong>
          {row.managerName ? <> is mapped to {row.managerName}.</> : <> has no manager chosen.</>}
        </p>
        <p className="advisers__hint">
          Removing it stops {row.managerName ?? 'anyone'} being told when a sign-off falls due
          for this adviser. Nothing else changes: the sign-off still works, any T&amp;C Manager
          can perform one, and nobody gains or loses access to any case.
        </p>

        <div className="advisers__actions">
          <button type="button" className="advisers__btn" onClick={confirm} disabled={removing}>
            {removing ? 'Removing…' : 'Remove mapping'}
          </button>
          <button
            type="button"
            className="advisers__btn advisers__btn--ghost"
            onClick={onClose}
            disabled={removing}
          >
            Cancel
          </button>
        </div>
      </div>
    </Modal>
  );
}
