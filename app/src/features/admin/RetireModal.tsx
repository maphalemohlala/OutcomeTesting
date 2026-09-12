import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import type { CommandResult } from '../../services/commands/commandClient';

/**
 * Confirms taking a question or a section out of the checklist (AD-122, AD-123).
 *
 * Nothing is deleted: retiring dates the content out, so the answers already recorded
 * against it keep resolving and a submitted review still reads as it was answered. The
 * reason is required because "why is this no longer asked" is precisely what a regulator
 * asks, and it is written to an immutable Audit Event.
 */

interface Props {
  subject: 'question' | 'section';
  /** What is being retired, as the administrator sees it named. */
  name: string;
  /** How many questions go with it. Sections only, and only when there are any. */
  questionCount?: number;
  onConfirm: (reason: string, effectiveTo: string) => Promise<CommandResult<unknown>>;
  onClose: () => void;
  onRetired: () => void;
}

/** Today as yyyy-MM-dd, which is what the commands take and what the columns hold. */
function today(): string {
  return new Date().toISOString().slice(0, 10);
}

export function RetireModal({
  subject,
  name,
  questionCount,
  onConfirm,
  onClose,
  onRetired,
}: Props) {
  const [reason, setReason] = useState('');
  const [effectiveTo, setEffectiveTo] = useState(today());
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit() {
    // Checked here as well as server-side so an empty reason costs a keystroke rather than
    // a round trip that comes back refused.
    if (!reason.trim()) {
      setError('Say why this is no longer part of the checklist. The reason is recorded on the audit trail.');
      return;
    }

    if (!effectiveTo) {
      setError('Choose the day it stops being asked.');
      return;
    }

    setBusy(true);
    setError(null);
    const result = await onConfirm(reason.trim(), effectiveTo);
    setBusy(false);

    if (result.ok) {
      onRetired();
      onClose();
    } else {
      setError(result.message);
    }
  }

  return (
    <Modal title={`Retire ${subject === 'section' ? 'section' : 'question'}`} onClose={onClose}>
      <div className="library__form">
        <p className="library__subject">{name}</p>

        <label className="library__field">
          <span>Stops being asked from</span>
          <input
            type="date"
            value={effectiveTo}
            min={today()}
            onChange={(e) => {
              setEffectiveTo(e.target.value);
              setError(null);
            }}
          />
        </label>

        <label className="library__field">
          <span>Reason</span>
          <textarea
            value={reason}
            onChange={(e) => {
              setReason(e.target.value);
              setError(null);
            }}
            rows={3}
          />
        </label>

        <p className="library__consequence" role="status">
          {subject === 'section'
            ? `Retiring the section takes ${
                questionCount === undefined || questionCount === 0
                  ? 'it'
                  : `it and its ${questionCount} question${questionCount === 1 ? '' : 's'}`
              } out of the form and out of the submit gate. Nothing is deleted: reviews already submitted still read as they were answered.`
            : 'The question stops being asked from that day. Nothing is deleted: the answers already given against it keep resolving, and a review already submitted still reads as it was answered.'}
        </p>

        {error ? (
          <p className="library__error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="library__form-actions">
          <button type="button" className="library__btn" onClick={onSubmit} disabled={busy}>
            {busy ? 'Retiring…' : `Retire ${subject}`}
          </button>
          <button
            type="button"
            className="library__btn library__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
        </div>
      </div>
    </Modal>
  );
}
