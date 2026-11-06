import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { updateCaseDetails } from '../../services/commands/updateCaseDetails';
import { canTransition, type CaseStatus } from '../../types/domain';

const NO_CHECK_REQUIRED = 120910592;

/** Removal is only open until the check is graded; the lifecycle decides that, not this page. */
export function canRemoveCase(status: CaseStatus | null | undefined): boolean {
  return Boolean(status) && status !== 'No Check Required' && canTransition(status, 'No Check Required');
}

interface Props {
  detail: { id: string; status: CaseStatus | null; rowVersion?: string | null };
  onRemoved: () => void;
}

/**
 * Removes a case added in error, or one that will not go ahead. Case data is kept, so this
 * takes the case out of checking rather than deleting it, and the reason goes on its history.
 */
export function CaseRemoveAction({ detail, onRemoved }: Props) {
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  if (!canRemoveCase(detail.status)) return null;

  async function remove() {
    const token = `remove:${detail.id}:${reason.trim()}`;
    setSaving(true);
    setError(null);
    const result = await updateCaseDetails({
      caseId: detail.id,
      status: NO_CHECK_REQUIRED,
      reason: reason.trim(),
      expectedRowVersion: detail.rowVersion,
      idempotencyKey: intent.keyFor(token),
    });
    setSaving(false);

    if (!result.ok) {
      setError(messageForFailure(result));
      return;
    }

    intent.release(token);
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
