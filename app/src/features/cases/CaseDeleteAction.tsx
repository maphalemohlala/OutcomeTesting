import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { useIntentKeys, type IntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { deleteCase } from '../../services/commands/deleteCase';

interface Props {
  detail: { id: string; caseReference: string };
}

export type DeletionOutcome = { kind: 'deleted' } | { kind: 'refused'; message: string };

/** Whether the typed reference names this case, as the server judges it: trimmed, any case. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export function referenceConfirmed(reference: string, typed: string): boolean {
  return reference.trim() !== '' && reference.trim().toLowerCase() === typed.trim().toLowerCase();
}

/** One delete attempt; the key follows the intent so a retry after a timeout replays. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export async function runDeletion(
  detail: Props['detail'],
  typed: string,
  reason: string,
  intent: IntentKeys,
): Promise<DeletionOutcome> {
  const token = `delete:${detail.id}:${reason.trim()}`;
  const result = await deleteCase({
    caseId: detail.id,
    confirmReference: typed.trim(),
    reason: reason.trim(),
    idempotencyKey: intent.keyFor(token),
  });

  if (!result.ok) return { kind: 'refused', message: messageForFailure(result) };

  intent.release(token);
  return { kind: 'deleted' };
}

/**
 * Deletes the case outright, at any stage (requirements log row 40). Unlike Remove case,
 * nothing is kept but the audit line, so the reference must be typed to confirm.
 */
export function CaseDeleteAction({ detail }: Props) {
  return (
    <PermissionGate resource="case.delete" need="Edit">
      <DeleteButton detail={detail} />
    </PermissionGate>
  );
}

function DeleteButton({ detail }: Props) {
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const [typed, setTyped] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function remove() {
    setSaving(true);
    setError(null);
    const outcome = await runDeletion(detail, typed, reason, intent);
    setSaving(false);

    if (outcome.kind === 'refused') {
      setError(outcome.message);
      return;
    }

    setOpen(false);
    navigate('/cases', { replace: true });
  }

  const ready = referenceConfirmed(detail.caseReference, typed) && reason.trim() !== '';

  return (
    <>
      <button type="button" className="case-edit__danger" onClick={() => setOpen(true)}>
        Delete case
      </button>

      {open ? (
        <Modal title="Delete this case" onClose={() => { if (!saving) setOpen(false); }}>
          <p className="case-edit__intro">
            This deletes the case and everything recorded against it: its checks and answers,
            outcomes, remedial actions, sign-offs, allocations, export rows and any letters not
            yet sent. It cannot be undone. Only a record that the case was deleted, by you and
            why, is kept. To take a case out of checking but keep it, use Remove case instead.
          </p>
          {error ? <Notice tone="error">{error}</Notice> : null}
          <label className="case-edit__field" htmlFor="case-delete-reference">
            <span>Type the case reference {detail.caseReference} to confirm</span>
            <input
              id="case-delete-reference"
              value={typed}
              autoComplete="off"
              onChange={(e) => setTyped(e.target.value)}
            />
          </label>
          <label className="case-edit__field" htmlFor="case-delete-reason">
            <span>Reason</span>
            <textarea
              id="case-delete-reason"
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
              className="case-edit__danger"
              disabled={saving || !ready}
              onClick={remove}
            >
              {saving ? 'Deleting…' : 'Delete case'}
            </button>
          </div>
        </Modal>
      ) : null}
    </>
  );
}
