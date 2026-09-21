import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { MIGRATED_LISTS, validateDraft, type ListOptionRow, type ManagedList } from './listOptions';
import {
  deleteListOption,
  reinstateListOption,
  retireListOption,
  saveListOption,
  useListOptions,
} from './useListOptions';
import './ListOptionsPage.css';

/**
 * The options of the case-header dropdowns, managed as data (project owner, 2026-09-21: "for
 * dropdown fields like Product / solution type, Sample source, Case type, and Pre or post
 * check - How easy would it be to create a management page where users can add, edit, and
 * remove options?").
 *
 * Product / solution type is the only list offered so far. The other three are declared in
 * listOptions.ts but have no lookup on the case yet, and MIGRATED_LISTS is what keeps them
 * off this page: offering a list whose chosen option has nowhere to be stored would be a
 * page that takes an administrator's work and drops it.
 *
 * "Remove" is two different things and the page says which is which. Retiring takes the
 * option out of every dropdown and leaves every case that already holds it reading exactly
 * as it did; deleting is only possible for an option no case has ever used, and it is
 * Dataverse that decides that, not this screen.
 */
export function ListOptionsPage() {
  const [list, setList] = useState<ManagedList>(MIGRATED_LISTS[0]);
  const [reloadKey, setReloadKey] = useState(0);
  const state = useListOptions(list, reloadKey);
  const { can } = usePermissions();
  const canEdit = can('page.admin.lists', 'Manage');

  const [editing, setEditing] = useState<ListOptionRow | 'new' | null>(null);
  const [removing, setRemoving] = useState<ListOptionRow | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  function reload() {
    setEditing(null);
    setRemoving(null);
    setReloadKey((k) => k + 1);
  }

  async function act(
    action: (id: string) => Promise<{ ok: true } | { ok: false; reason: string }>,
    row: ListOptionRow,
  ) {
    setProblem(null);
    const result = await action(row.id);
    if (result.ok) {
      reload();
      return;
    }
    setRemoving(null);
    setProblem(result.reason);
  }

  return (
    <div className="lists">
      <PageIntro
        title="Dropdown options"
        purpose={
          'The choices offered by the case header’s dropdowns. Adding or renaming one takes ' +
          'effect straight away — there is nothing to deploy.'
        }
      />

      {MIGRATED_LISTS.length > 1 && (
        <div className="lists__picker">
          <label htmlFor="which-list">List</label>
          <select
            id="which-list"
            value={list.key}
            onChange={(e) =>
              setList(MIGRATED_LISTS.find((l) => l.key === e.target.value) ?? MIGRATED_LISTS[0])
            }
          >
            {MIGRATED_LISTS.map((l) => (
              <option key={l.key} value={l.key}>
                {l.label}
              </option>
            ))}
          </select>
        </div>
      )}

      <h2 className="lists__heading">{list.label}</h2>

      {problem && (
        <p className="lists__problem" role="alert">
          {problem}
        </p>
      )}

      {state.status === 'loading' && <p className="lists__note">Loading…</p>}

      {state.status === 'unavailable' && <p className="lists__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <>
          {canEdit && (
            <button type="button" className="lists__btn lists__add" onClick={() => setEditing('new')}>
              Add an option
            </button>
          )}

          {state.options.length === 0 ? (
            <p className="lists__note">
              This list has no options yet, so the dropdown on a case is empty.
            </p>
          ) : (
            <table className="lists__table">
              <thead>
                <tr>
                  <th scope="col">Option</th>
                  <th scope="col">Order</th>
                  <th scope="col">Offered</th>
                  {canEdit && (
                    <th scope="col">
                      <span className="lists__sr">Actions</span>
                    </th>
                  )}
                </tr>
              </thead>
              <tbody>
                {state.options.map((row) => (
                  <tr key={row.id} className={row.retired ? 'lists__row--retired' : undefined}>
                    <td>{row.label}</td>
                    <td>{row.sortOrder ?? <span className="lists__missing">—</span>}</td>
                    <td>
                      {row.retired ? (
                        <span className="lists__retired">
                          Retired{row.effectiveTo ? ` on ${row.effectiveTo}` : ''}
                        </span>
                      ) : (
                        'Yes'
                      )}
                    </td>
                    {canEdit && (
                      <td className="lists__actions">
                        <button
                          type="button"
                          className="lists__btn lists__btn--ghost"
                          onClick={() => setEditing(row)}
                        >
                          Change
                        </button>
                        {row.retired ? (
                          <button
                            type="button"
                            className="lists__btn lists__btn--ghost"
                            onClick={() => act(reinstateListOption, row)}
                          >
                            Reinstate
                          </button>
                        ) : (
                          <button
                            type="button"
                            className="lists__btn lists__btn--ghost"
                            onClick={() => setRemoving(row)}
                          >
                            Remove
                          </button>
                        )}
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}

          <p className="lists__hint">
            Retired options stay on this page so an option that was removed last week is not
            added again by somebody who cannot see that it ever existed. Cases that already
            use a retired option are not changed by retiring it.
          </p>

          {editing && (
            <OptionForm
              list={list}
              row={editing === 'new' ? null : editing}
              siblings={state.options}
              onClose={() => setEditing(null)}
              onSaved={reload}
            />
          )}

          {removing && (
            <RemoveOption
              row={removing}
              onClose={() => setRemoving(null)}
              onRetire={() => act(retireListOption, removing)}
              onDelete={() => act(deleteListOption, removing)}
            />
          )}
        </>
      )}
    </div>
  );
}

function OptionForm({
  list,
  row,
  siblings,
  onClose,
  onSaved,
}: {
  list: ManagedList;
  row: ListOptionRow | null;
  /** What the list already holds, so a duplicate is named here rather than guessed at. */
  siblings: ListOptionRow[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [label, setLabel] = useState(row?.label ?? '');
  const [sortOrder, setSortOrder] = useState(row?.sortOrder == null ? '' : String(row.sortOrder));
  const [effectiveFrom, setEffectiveFrom] = useState(row?.effectiveFrom ?? '');
  const [effectiveTo, setEffectiveTo] = useState(row?.effectiveTo ?? '');
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setProblem(null);

    const draft = {
      id: row?.id,
      label,
      sortOrder,
      effectiveFrom: effectiveFrom === '' ? null : effectiveFrom,
      effectiveTo: effectiveTo === '' ? null : effectiveTo,
    };

    const refusal = validateDraft(draft, siblings);
    if (refusal) {
      setProblem(refusal);
      return;
    }

    setSaving(true);
    const result = await saveListOption(list, draft);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  return (
    <Modal title={row ? 'Change option' : 'Add an option'} onClose={onClose}>
      <form className="lists__form" onSubmit={submit}>
        <label htmlFor="option-label">Option</label>
        <input
          id="option-label"
          type="text"
          value={label}
          maxLength={200}
          onChange={(e) => setLabel(e.target.value)}
        />
        <p className="lists__hint">
          What a checker sees in the dropdown. Renaming an option renames it on every case
          that already has it, which is usually what you want — the history follows the name
          rather than splitting at it.
        </p>

        <label htmlFor="option-order">Sort order</label>
        <input
          id="option-order"
          type="text"
          inputMode="numeric"
          value={sortOrder}
          onChange={(e) => setSortOrder(e.target.value)}
        />
        <p className="lists__hint">
          Optional. Leave it blank and the option goes to the end of the list.
        </p>

        <label htmlFor="option-from">Offered from</label>
        <input
          id="option-from"
          type="date"
          value={effectiveFrom}
          onChange={(e) => setEffectiveFrom(e.target.value)}
        />

        <label htmlFor="option-to">Offered until</label>
        <input
          id="option-to"
          type="date"
          value={effectiveTo}
          onChange={(e) => setEffectiveTo(e.target.value)}
        />
        <p className="lists__hint">
          Both optional. Setting an end date is the same as retiring the option on that day.
        </p>

        {problem && (
          <p className="lists__problem" role="alert">
            {problem}
          </p>
        )}

        <div className="lists__buttons">
          <button type="button" className="lists__btn lists__btn--ghost" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="lists__btn" disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </Modal>
  );
}

/**
 * Retire or delete, said plainly, because they are not the same act and the difference is
 * not recoverable in one direction.
 */
function RemoveOption({
  row,
  onClose,
  onRetire,
  onDelete,
}: {
  row: ListOptionRow;
  onClose: () => void;
  onRetire: () => void;
  onDelete: () => void;
}) {
  return (
    <Modal title={`Remove “${row.label}”`} onClose={onClose}>
      <p>
        <strong>Retire it</strong> to stop offering it on new cases. Cases that already have
        it keep reading exactly as they do now, and you can put it back at any time.
      </p>
      <p>
        <strong>Delete it</strong> only if it was added by mistake. This works only when no
        case has ever used the option — if any case has, Dataverse refuses the delete and
        nothing is changed.
      </p>
      <div className="lists__buttons">
        <button type="button" className="lists__btn lists__btn--ghost" onClick={onClose}>
          Cancel
        </button>
        <button type="button" className="lists__btn lists__btn--danger" onClick={onDelete}>
          Delete permanently
        </button>
        <button type="button" className="lists__btn" onClick={onRetire}>
          Retire
        </button>
      </div>
    </Modal>
  );
}
