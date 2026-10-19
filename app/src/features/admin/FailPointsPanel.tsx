import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import {
  FAIL_REASON_CATEGORIES,
  nextDisplayOrder,
  validateFailReasonDraft,
  type FailReasonRow,
} from './failReasons';
import { reinstateFailReason, retireFailReason, saveFailReason, useFailReasons } from './useFailReasons';

/**
 * The File Quality fail points the checklist ticks, managed beside the questions they sit
 * among. Both teams see the whole list (one undivided block on the checklist); the category
 * groups the points for MI, it does not decide who sees them.
 */
export function FailPointsPanel({ canEdit }: { canEdit: boolean }) {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useFailReasons(reloadKey);
  const [editing, setEditing] = useState<FailReasonRow | 'new' | null>(null);
  const [retiring, setRetiring] = useState<FailReasonRow | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  function reload() {
    setEditing(null);
    setRetiring(null);
    setReloadKey((k) => k + 1);
  }

  async function act(action: (id: string) => Promise<{ ok: true } | { ok: false; reason: string }>, row: FailReasonRow) {
    setProblem(null);
    const result = await action(row.id);
    if (result.ok) return reload();
    setRetiring(null);
    setProblem(result.reason);
  }

  const rows = state.status === 'ready' ? state.rows : [];
  const live = rows.filter((row) => !row.retired);
  const retired = rows.filter((row) => row.retired);

  const renderRow = (row: FailReasonRow) => (
    <li key={row.id} className="library__question" data-retired={row.retired ? 'true' : 'false'}>
      <p className="library__wording">{row.name}</p>
      <div className="library__question-meta">
        <span className="library__code">{row.code}</span>
        <span className="library__response">{row.categoryLabel}</span>
        <span className="library__muted">Order {row.order}</span>
        {row.retired ? <span className="library__flag">Retired</span> : null}
        {canEdit && !row.retired ? (
          <>
            <button type="button" className="library__btn library__btn--ghost" onClick={() => setEditing(row)}>
              Edit fail point
            </button>
            <button type="button" className="library__btn library__btn--ghost" onClick={() => setRetiring(row)}>
              Retire
            </button>
          </>
        ) : null}
        {canEdit && row.retired ? (
          <button type="button" className="library__btn library__btn--ghost" onClick={() => act(reinstateFailReason, row)}>
            Reinstate
          </button>
        ) : null}
      </div>
    </li>
  );

  return (
    <section className="library__section" aria-labelledby="fail-points-heading">
      <header className="library__section-head">
        <h2 id="fail-points-heading">File Quality – Fail points</h2>
        <div className="library__section-meta">
          <span className="library__owner">Both</span>
          <span className="library__muted">
            {live.length} {live.length === 1 ? 'fail point' : 'fail points'}
          </span>
          {canEdit && state.status === 'ready' ? (
            <button type="button" className="library__btn library__btn--ghost" onClick={() => setEditing('new')}>
              Add fail point
            </button>
          ) : null}
        </div>
      </header>

      {problem ? (
        <p className="library__error" role="alert">
          {problem}
        </p>
      ) : null}
      {state.status === 'loading' ? <p role="status">Loading the fail points…</p> : null}
      {state.status === 'unavailable' ? <p className="library__muted">{state.reason}</p> : null}

      {state.status === 'ready' ? (
        <>
          {live.length === 0 ? (
            <p className="library__muted">No fail points, so the checklist shows an empty block.</p>
          ) : (
            <ol className="library__questions">{live.map(renderRow)}</ol>
          )}
          {retired.length > 0 ? (
            <details className="library__retired">
              <summary>Retired ({retired.length})</summary>
              <ol className="library__questions">{retired.map(renderRow)}</ol>
            </details>
          ) : null}
        </>
      ) : null}

      {editing ? (
        <FailPointModal row={editing === 'new' ? null : editing} rows={rows} onClose={() => setEditing(null)} onSaved={reload} />
      ) : null}

      {retiring ? (
        <Modal title={`Retire ${retiring.code}`} onClose={() => setRetiring(null)}>
          <div className="library__form">
            <p className="library__subject">{retiring.name}</p>
            <p>
              It leaves every check still in progress straight away, including any that ticked it,
              and is not counted when those checks are submitted. Checks already submitted keep
              showing it as they were. You can reinstate it at any time.
            </p>
            <div className="library__form-actions">
              <button type="button" className="library__btn library__btn--ghost" onClick={() => setRetiring(null)}>
                Cancel
              </button>
              <button type="button" className="library__btn" onClick={() => act(retireFailReason, retiring)}>
                Retire
              </button>
            </div>
          </div>
        </Modal>
      ) : null}
    </section>
  );
}

function FailPointModal({
  row,
  rows,
  onClose,
  onSaved,
}: {
  row: FailReasonRow | null;
  rows: FailReasonRow[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState(row?.name ?? '');
  const [category, setCategory] = useState<number>(row?.category ?? FAIL_REASON_CATEGORIES[0].value);
  const [order, setOrder] = useState(String(row?.order ?? nextDisplayOrder(rows)));
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSave() {
    const draft = { id: row?.id, name, category, order };
    const refusal = validateFailReasonDraft(draft, rows);
    if (refusal) return setError(refusal);
    setBusy(true);
    const result = await saveFailReason(draft, rows);
    setBusy(false);
    if (result.ok) return onSaved();
    setError(result.reason);
  }

  return (
    <Modal title={row ? `Edit ${row.code}` : 'Add a fail point'} onClose={onClose}>
      <div className="library__form">
        <label className="library__field">
          <span>Wording</span>
          <input type="text" value={name} maxLength={200} onChange={(e) => setName(e.target.value)} placeholder="Record Keeping - …" />
        </label>
        <p className="library__muted">
          Exactly as the checklist should show it, category prefix included.
        </p>
        {row ? (
          <p className="library__warning" role="status">
            Changing the wording changes it on every review that ticked this point, submitted ones
            included. To change what the point means, retire it and add a new one instead.
          </p>
        ) : null}

        <label className="library__field">
          <span>Category</span>
          <select value={category} onChange={(e) => setCategory(Number(e.target.value))}>
            {FAIL_REASON_CATEGORIES.map((c) => (
              <option key={c.value} value={c.value}>
                {c.label}
              </option>
            ))}
          </select>
        </label>

        <label className="library__field">
          <span>Display order</span>
          <input type="number" min={0} value={order} onChange={(e) => setOrder(e.target.value)} />
        </label>

        {error ? (
          <p className="library__error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="library__form-actions">
          <button type="button" className="library__btn library__btn--ghost" onClick={onClose} disabled={busy}>
            Cancel
          </button>
          <button type="button" className="library__btn" onClick={onSave} disabled={busy}>
            {busy ? 'Saving…' : row ? 'Save changes' : 'Add fail point'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
