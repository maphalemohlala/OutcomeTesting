import { useEffect, useRef, useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { Icon } from '../../components/icons/Icon';
import { date } from '../../lib/format';
import {
  MIGRATED_LISTS,
  offeredOptions,
  optionCanBeDeleted,
  validateDraft,
  type ListOptionRow,
  type ManagedList,
} from './listOptions';
import { reorderedSortOrders } from './listOrder';
import {
  deleteListOption,
  reinstateListOption,
  retireListOption,
  saveListOption,
  useListOptions,
} from './useListOptions';
import './ListOptionsPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';
import { Pager } from '../../components/navigation/Pager';
import { PAGE_SIZE, usePaged } from '../../components/navigation/paging';

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
 * as it did. Deleting is offered only where Dataverse can refuse it: an option held through a
 * lookup that no case currently holds. Products sit on a many-to-many, which has no such
 * guard, and a migrated option can be held by a case's old value alone.
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

  /** A new option may have taken the name since this one was retired. */
  function reinstate(row: ListOptionRow, siblings: readonly ListOptionRow[]) {
    const clash = validateDraft(
      { id: row.id, label: row.label, sortOrder: '', effectiveFrom: null, effectiveTo: null },
      siblings,
    );
    if (clash) setProblem(clash);
    else void act(reinstateListOption, row);
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

  const [moving, setMoving] = useState(false);
  const [moved, setMoved] = useState('');
  const dragFrom = useRef<number | null>(null);
  // The handle to put focus back on once the list has reloaded in its new order.
  const focusAfter = useRef<string | null>(null);

  useEffect(() => {
    if (state.status !== 'ready' || !focusAfter.current) return;
    document.querySelector<HTMLButtonElement>(`[data-handle="${focusAfter.current}"]`)?.focus();
    focusAfter.current = null;
  }, [state]);

  /** Renumbers the offered options in their new order, writing only the ones whose number changes. */
  async function move(offered: readonly ListOptionRow[], from: number, to: number) {
    const changes = reorderedSortOrders(offered, from, to);
    if (changes.length === 0 || moving) return;
    setMoving(true);
    setProblem(null);
    setMoved('');
    let failed: string | null = null;
    for (const { row, sortOrder } of changes) {
      const result = await saveListOption(list, {
        id: row.id,
        label: row.label,
        sortOrder: String(sortOrder),
        effectiveFrom: row.effectiveFrom,
        effectiveTo: row.effectiveTo,
      });
      if (!result.ok) {
        failed = result.reason;
        break;
      }
    }
    setMoving(false);
    if (failed) setProblem(`The new order was not fully saved, so the list below shows what was. ${failed}`);
    else setMoved(`${offered[from].label} moved to position ${to + 1}.`);
    focusAfter.current = offered[from].id;
    setReloadKey((k) => k + 1);
  }

  const options = state.status === 'ready' ? state.options : [];
  const offered = offeredOptions(options);
  const retired = options.filter((row) => row.retired && !row.startsOn);
  const upcoming = options.filter((row) => row.startsOn);
  const offeredPage = usePaged(offered, PAGE_SIZE, list.label);
  const upcomingPage = usePaged(upcoming, PAGE_SIZE, list.label);
  const retiredPage = usePaged(retired, PAGE_SIZE, list.label);

  return (
    <div className="lists">
      <PageIntro
        title="Dropdown options"
        purpose={
          'The choices offered by the case header’s dropdowns. Adding or renaming one takes ' +
          'effect straight away; there is nothing to deploy.'
        }
        actions={
          canEdit && state.status === 'ready' ? (
            <button type="button" className="button-primary" onClick={() => setEditing('new')}>
              <Icon name="plus" />
              Add an option
            </button>
          ) : undefined
        }
      />

      <FilterBar
        summary={
          state.status === 'ready' ? `${offered.length} offered, ${retired.length} retired` : undefined
        }
      >
        <FilterField label="List" htmlFor="which-list">
          <select
            id="which-list"
            value={list.key}
            onChange={(e) => setList(MIGRATED_LISTS.find((l) => l.key === e.target.value) ?? MIGRATED_LISTS[0])}
          >
            {MIGRATED_LISTS.map((l) => (
              <option key={l.key} value={l.key}>
                {l.label}
              </option>
            ))}
          </select>
        </FilterField>
      </FilterBar>

      {problem && (
        <p className="lists__problem" role="alert">
          {problem}
        </p>
      )}
      <p className="lists__sr" role="status">
        {moving ? 'Saving the new order…' : moved}
      </p>

      {state.status === 'loading' && <Skeleton label="Loading list options…" rows={6} />}

      {state.status === 'unavailable' && <p className="lists__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <div className="lists__layout">
          <div className="lists__main">
            <section aria-labelledby="lists-offered">
              <p className="lists__section-head">
                <strong id="lists-offered">Offered</strong>
                {canEdit && offered.length > 1 ? (
                  <span className="lists__muted">Drag to change the order checkers see.</span>
                ) : null}
              </p>
              {offered.length === 0 ? (
                <p className="lists__note">This list has no options offered, so the dropdown on a case is empty.</p>
              ) : (
                <ol className="lists__rows" aria-busy={moving}>
                  {offeredPage.rows.map((row, i) => {
                    // Positions count across the whole list, not this page.
                    const index = offeredPage.from - 1 + i;
                    return (
                    <li
                      key={row.id}
                      className="lists__row"
                      draggable={canEdit && !moving}
                      onDragStart={(e) => {
                        dragFrom.current = index;
                        e.dataTransfer.effectAllowed = 'move';
                      }}
                      onDragOver={(e) => {
                        if (dragFrom.current !== null) e.preventDefault();
                      }}
                      onDrop={(e) => {
                        e.preventDefault();
                        const from = dragFrom.current;
                        dragFrom.current = null;
                        if (from !== null) void move(offered, from, index);
                      }}
                      onDragEnd={() => {
                        dragFrom.current = null;
                      }}
                    >
                      {canEdit && offered.length > 1 ? (
                        <button
                          type="button"
                          className="lists__handle"
                          data-handle={row.id}
                          disabled={moving}
                          aria-label={`Move ${row.label}, position ${index + 1} of ${offered.length}. Press the up or down arrow to move it.`}
                          onKeyDown={(e) => {
                            const to = e.key === 'ArrowUp' ? index - 1 : e.key === 'ArrowDown' ? index + 1 : null;
                            if (to === null) return;
                            e.preventDefault();
                            if (to < 0 || to >= offered.length) return;
                            // Follow the option onto the next page so focus can land back on it.
                            offeredPage.setPage(Math.floor(to / PAGE_SIZE) + 1);
                            void move(offered, index, to);
                          }}
                        >
                          <Icon name="drag" />
                        </button>
                      ) : (
                        <span className="lists__handle-space" />
                      )}
                      <span className="lists__position">{index + 1}</span>
                      <span className="lists__label">{row.label}</span>
                      <span className="lists__row-note">
                        {row.sortOrder === null ? (
                          <span className="lists__chip lists__chip--warn">
                            <Icon name="warning" />
                            No order set, so it shows last
                          </span>
                        ) : null}
                      </span>
                      {canEdit ? (
                        <span className="lists__row-actions">
                          <button type="button" className="button-secondary" onClick={() => setEditing(row)}>
                            Rename<span className="lists__sr"> {row.label}</span>
                          </button>
                          <button type="button" className="button-quiet" onClick={() => setRemoving(row)}>
                            Retire<span className="lists__sr"> {row.label}</span>
                          </button>
                        </span>
                      ) : null}
                    </li>
                    );
                  })}
                </ol>
              )}
              {offered.length > 0 && <Pager {...offeredPage} label="options" />}
            </section>

            {upcoming.length > 0 ? (
              <section aria-labelledby="lists-upcoming">
                <p className="lists__section-head">
                  <strong id="lists-upcoming">Not offered yet</strong>
                  <span className="lists__muted">Each starts being offered on the day shown.</span>
                </p>
                <ul className="lists__rows">
                  {upcomingPage.rows.map((row) => (
                    <li key={row.id} className="lists__row">
                      <span className="lists__handle-space" />
                      <span className="lists__position" />
                      <span className="lists__label">{row.label}</span>
                      <span className="lists__row-note lists__muted">Starts on {date(row.startsOn) ?? row.startsOn}</span>
                      {canEdit ? (
                        <span className="lists__row-actions">
                          <button type="button" className="button-secondary" onClick={() => setEditing(row)}>
                            Rename<span className="lists__sr"> {row.label}</span>
                          </button>
                        </span>
                      ) : null}
                    </li>
                  ))}
                </ul>
                <Pager {...upcomingPage} label="options" />
              </section>
            ) : null}

            {retired.length > 0 ? (
              <section aria-labelledby="lists-retired">
                <p className="lists__section-head">
                  <strong id="lists-retired">Retired</strong>
                  <span className="lists__muted">
                    Kept here so a removed option isn’t added again by someone who never saw it. Cases already using it
                    are unchanged.
                  </span>
                </p>
                <ul className="lists__rows">
                  {retiredPage.rows.map((row) => (
                    <li key={row.id} className="lists__row lists__row--retired">
                      <span className="lists__handle-space" />
                      <span className="lists__position" />
                      <span className="lists__label">
                        <s>{row.label}</s>
                        <span className="lists__sr"> (retired)</span>
                      </span>
                      <span className="lists__row-note lists__muted">
                        {row.effectiveTo ? `Retired on ${date(row.effectiveTo) ?? row.effectiveTo}` : null}
                      </span>
                      {canEdit ? (
                        <span className="lists__row-actions">
                          <button
                            type="button"
                            className="button-secondary"
                            onClick={() => reinstate(row, state.options)}
                          >
                            <Icon name="restore" />
                            Reinstate<span className="lists__sr"> {row.label}</span>
                          </button>
                        </span>
                      ) : null}
                    </li>
                  ))}
                </ul>
                <Pager {...retiredPage} label="options" />
              </section>
            ) : null}
          </div>

          <aside className="lists__preview" aria-labelledby="lists-preview">
            <h2 id="lists-preview">On the case header</h2>
            <label htmlFor="lists-preview-select">{list.label}</label>
            <select id="lists-preview-select" defaultValue="">
              <option value="">Not set</option>
              {offered.map((row) => (
                <option key={row.id} value={row.id}>
                  {row.label}
                </option>
              ))}
            </select>
            <p className="lists__muted">This is what a checker sees right now.</p>
          </aside>

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
              deletable={optionCanBeDeleted(list, removing)}
              onClose={() => setRemoving(null)}
              onRetire={() => act(retireListOption, removing)}
              onDelete={() => act(deleteListOption, removing)}
            />
          )}
        </div>
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
  deletable,
  onClose,
  onRetire,
  onDelete,
}: {
  row: ListOptionRow;
  deletable: boolean;
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
      {deletable ? (
        <p>
          <strong>Delete it</strong> only if it was added by mistake. This works only when no
          case currently holds the option — if any case does, Dataverse refuses the delete and
          nothing is changed.
        </p>
      ) : (
        <p>This option cannot be deleted, because nothing would stop that removing it from cases.</p>
      )}
      <div className="lists__buttons">
        <button type="button" className="lists__btn lists__btn--ghost" onClick={onClose}>
          Cancel
        </button>
        {deletable ? (
          <button type="button" className="lists__btn lists__btn--danger" onClick={onDelete}>
            Delete permanently
          </button>
        ) : null}
        <button type="button" className="lists__btn" onClick={onRetire}>
          Retire
        </button>
      </div>
    </Modal>
  );
}
