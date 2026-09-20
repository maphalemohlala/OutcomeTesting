import { useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import {
  useAdviserMappings,
  saveAdviserMapping,
  type AdviserMappingRow,
  type ManagerOption,
} from './useAdviserMappings';
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
            <button type="button" className="advisers__add" onClick={() => setEditing('new')}>
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
                        <button type="button" onClick={() => setEditing(row)}>
                          Change
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          {editing && (
            <MappingForm
              row={editing === 'new' ? null : editing}
              managers={state.managers}
              onClose={() => setEditing(null)}
              onSaved={() => {
                setEditing(null);
                setReloadKey((k) => k + 1);
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
  managers,
  onClose,
  onSaved,
}: {
  row: AdviserMappingRow | null;
  managers: ManagerOption[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [adviserEmail, setAdviserEmail] = useState(row?.adviserEmail ?? '');
  const [managerId, setManagerId] = useState(row?.managerId ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setProblem(null);

    const result = await saveAdviserMapping(row?.id ?? null, adviserEmail, managerId);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  return (
    <form className="advisers__form" onSubmit={submit}>
      <h2>{row ? 'Change mapping' : 'Map an adviser'}</h2>

      <label htmlFor="adviser-email">Adviser’s work email</label>
      <input
        id="adviser-email"
        type="email"
        value={adviserEmail}
        // Read-only when changing an existing row: the email is the alternate key, so
        // editing it here would silently move the mapping to a different adviser rather
        // than correct this one.
        readOnly={row !== null}
        onChange={(e) => setAdviserEmail(e.target.value)}
      />
      <p className="advisers__hint">
        As it appears on the case. The import takes it from the extract’s AdviserEmail column.
      </p>

      <label htmlFor="manager">T&amp;C Manager</label>
      <select id="manager" value={managerId} onChange={(e) => setManagerId(e.target.value)}>
        <option value="">Choose…</option>
        {managers.map((m) => (
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
        <button type="submit" disabled={saving}>
          {saving ? 'Saving…' : 'Save'}
        </button>
        <button type="button" onClick={onClose} disabled={saving}>
          Cancel
        </button>
      </div>
    </form>
  );
}
