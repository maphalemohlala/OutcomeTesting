import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { Notice } from '../../components/feedback/Notice';
import { PermissionGate } from '../../app/permissions/PermissionGate';
import { useIntentKeys, type IntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import { ADVICE_GRADES, FILE_GRADES, amendOutcome } from '../../services/commands/amendOutcome';

interface Props {
  caseId: string;
  /** The Advice grade first given, from the case's newest outcome; null when no AQS check graded it. */
  adviceGrade: string | null;
  onSaved: () => void;
}

export type AmendOutcomeResult = { kind: 'amended'; summary: string } | { kind: 'refused'; message: string };

/** One amend attempt; the key follows the intent so a retry after a timeout replays. */
// eslint-disable-next-line react-refresh/only-export-components -- exported for its test
export async function runAmendment(
  caseId: string,
  fileGrade: string,
  adviceGrade: string,
  reason: string,
  intent: IntentKeys,
): Promise<AmendOutcomeResult> {
  const token = `amend:${caseId}:${fileGrade}:${adviceGrade}:${reason.trim()}`;
  const result = await amendOutcome({
    caseId,
    fileGrade,
    adviceGrade,
    reason: reason.trim(),
    idempotencyKey: intent.keyFor(token),
  });

  if (!result.ok) return { kind: 'refused', message: messageForFailure(result) };

  intent.release(token);
  return { kind: 'amended', summary: result.data?.Summary ?? 'The outcome was amended.' };
}

/**
 * Amends the grades a check first gave the case, at any stage, for an appeal (requirements
 * log row 39). The grade only: the case stays where it is.
 */
export function CaseAmendOutcomeAction(props: Props) {
  return (
    <PermissionGate resource="case.amendoutcome" need="Edit">
      <AmendButton {...props} />
    </PermissionGate>
  );
}

function AmendButton({ caseId, adviceGrade, onSaved }: Props) {
  const [open, setOpen] = useState(false);
  const [fileGrade, setFileGrade] = useState('');
  const [newAdvice, setNewAdvice] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);
  const intent = useIntentKeys();

  async function save() {
    setSaving(true);
    setError(null);
    const outcome = await runAmendment(caseId, fileGrade, newAdvice, reason, intent);
    setSaving(false);

    if (outcome.kind === 'refused') {
      setError(outcome.message);
      return;
    }

    setOpen(false);
    setFileGrade('');
    setNewAdvice('');
    setReason('');
    setDone(outcome.summary);
    onSaved();
  }

  const ready = (fileGrade !== '' || newAdvice !== '') && reason.trim() !== '';

  return (
    <div className="case-detail__amend">
      <button type="button" className="case-edit__cancel" onClick={() => { setDone(null); setOpen(true); }}>
        Amend outcome
      </button>
      {done ? <Notice tone="success">{done}</Notice> : null}

      {open ? (
        <Modal title="Amend the outcome" onClose={() => { if (!saving) setOpen(false); }}>
          <p className="case-edit__intro">
            This changes the grades the check first gave the case, for example after an appeal. The
            case stays where it is: a fail changed to a pass is not closed, and a pass changed to a
            fail is not reopened, so finish or stop its remedial actions and sign-off yourself. A
            final outcome already recorded by a regrade still counts as the grade in force. The old
            and new grades and your reason are kept in the case&apos;s History.
          </p>
          {error ? <Notice tone="error">{error}</Notice> : null}
          <label className="case-edit__field" htmlFor="case-amend-file">
            <span>File grade</span>
            <select id="case-amend-file" value={fileGrade} onChange={(e) => setFileGrade(e.target.value)}>
              <option value="">Leave as it is</option>
              {FILE_GRADES.map((grade) => (
                <option key={grade} value={grade}>{grade}</option>
              ))}
            </select>
          </label>
          <label className="case-edit__field" htmlFor="case-amend-advice">
            <span>Advice grade{adviceGrade ? ` (now ${adviceGrade})` : ''}</span>
            <select
              id="case-amend-advice"
              value={newAdvice}
              disabled={!adviceGrade}
              onChange={(e) => setNewAdvice(e.target.value)}
            >
              <option value="">{adviceGrade ? 'Leave as it is' : 'No AQS check has graded this case'}</option>
              {ADVICE_GRADES.map((grade) => (
                <option key={grade} value={grade}>{grade}</option>
              ))}
            </select>
          </label>
          <label className="case-edit__field" htmlFor="case-amend-reason">
            <span>Reason</span>
            <textarea id="case-amend-reason" value={reason} required onChange={(e) => setReason(e.target.value)} />
          </label>
          <div className="case-edit__actions">
            <button type="button" className="case-edit__cancel" disabled={saving} onClick={() => setOpen(false)}>
              Cancel
            </button>
            <button type="button" className="case-edit__save" disabled={saving || !ready} onClick={save}>
              {saving ? 'Saving…' : 'Amend outcome'}
            </button>
          </div>
        </Modal>
      ) : null}
    </div>
  );
}
