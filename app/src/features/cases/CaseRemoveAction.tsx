import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { useIntentKeys, type IntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { updateCaseDetails } from '../../services/commands/updateCaseDetails';
import type { CaseStatus } from '../../types/domain';
import { canRemoveCase } from './caseRemoval';

const NO_CHECK_REQUIRED = 120910592;

interface Props {
  detail: {
    id: string;
    status: CaseStatus | null;
    rowVersion?: string | null;
    checklist?: { items: string[] };
  };
  onRemoved: () => void;
  /** Re-reads the case. Called after a conflict, so the next try carries the fresh row version. */
  onStale?: () => void;
}

export type RemovalOutcome = { kind: 'removed' } | { kind: 'refused'; message: string; reread: boolean };

/** One removal attempt. A conflict asks for a re-read: every retry on the old version would fail too. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export async function runRemoval(
  detail: Props['detail'],
  reason: string,
  intent: IntentKeys,
): Promise<RemovalOutcome> {
  const token = `remove:${detail.id}:${reason.trim()}`;
  const result = await updateCaseDetails({
    caseId: detail.id,
    status: NO_CHECK_REQUIRED,
    reason: reason.trim(),
    expectedRowVersion: detail.rowVersion,
    idempotencyKey: intent.keyFor(token),
  });

  if (!result.ok) {
    return { kind: 'refused', message: messageForFailure(result), reread: result.kind === 'conflict' };
  }

  intent.release(token);
  return { kind: 'removed' };
}

/**
 * Removes a case added in error, or one that will not go ahead. Case data is kept, so this
 * takes the case out of checking rather than deleting it, and the reason goes on its history.
 */
export function CaseRemoveAction({ detail, onRemoved, onStale }: Props) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  if (!canRemoveCase(detail.status)) return null;
  // The server refuses this too (CaseStatusGuardPlugin); here it only saves a wasted try.
  if (detail.checklist?.items.some((item) => item.trim().toLowerCase() === 'high risk item 1')) {
    return (
      <p className="case-card__note">
        Tier 1 high-risk case (High Risk Item 1): it is always checked, so it cannot be removed.
      </p>
    );
  }

  async function remove() {
    setSaving(true);
    setError(null);
    const outcome = await runRemoval(detail, reason, intent);
    setSaving(false);

    if (outcome.kind === 'refused') {
      setError(outcome.message);
      if (outcome.reread) onStale?.();
      return;
    }

    setOpen(false);
    onRemoved();
  }

  return (
    <>
      <button type="button" className="case-edit__cancel" onClick={() => setOpen(true)}>
        Remove case
      </button>

      {open ? (
        <Modal title="Remove this case" onClose={() => { if (!saving) setOpen(false); }}>
          <p className="case-edit__intro">
            Use this when the case was added in error or will not go ahead. Nobody will be able
            to check it and it leaves the worklist, but it stays on record with your reason. This
            cannot be undone, and uploading the same task again will not bring it back.
          </p>
          {error ? <Notice tone="error">{error}</Notice> : null}
          <label className="case-edit__field" htmlFor="case-remove-reason">
            <span>Reason</span>
            <textarea
              id="case-remove-reason"
              value={reason}
              required
              onChange={(e) => setReason(e.target.value)}
            />
          </label>
          <div className="case-edit__actions">
            <button
              type="button"
              className="case-edit__cancel"
              disabled={saving}
              onClick={() => setOpen(false)}
            >
              Keep case
            </button>
            <button
              type="button"
              className="case-edit__save"
              disabled={saving || !reason.trim()}
              onClick={remove}
            >
              {saving ? 'Removing…' : 'Remove case'}
            </button>
          </div>
        </Modal>
      ) : null}
    </>
  );
}
