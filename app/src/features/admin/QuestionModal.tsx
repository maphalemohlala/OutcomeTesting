import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { addQuestion, moveQuestion, retireAndSucceedQuestion } from '../../services/commands/questions';
import { intentFor, type QuestionDraft } from './questionModalIntent';
import { RESPONSE_TYPE_OPTIONS, type LibraryQuestion, type LibrarySection } from './useQuestionLibrary';

/**
 * The question editor (AD-122). Add creates a question and its first version; edit dispatches
 * by what changed — a section change is a move, anything else is a new version.
 *
 * Nothing here is a security boundary: every control is mirrored by a server-side refusal and
 * the plug-in is the gate (AD-041).
 */

interface Props {
  mode: 'add' | 'edit';
  /** Sections in force, for the section picker and for add. */
  sections: LibrarySection[];
  /** The section the row sits in — fixed in add mode, the starting value in edit mode. */
  sectionId: string;
  /** The question being edited. Absent in add mode. */
  question?: LibraryQuestion;
  onClose: () => void;
  onSaved: () => void;
}

const DEFAULT_RESPONSE_TYPE = RESPONSE_TYPE_OPTIONS[0]?.value ?? 120910006;

export function QuestionModal({ mode, sections, sectionId, question, onClose, onSaved }: Props) {
  const original: QuestionDraft = {
    wording: question?.wording ?? '',
    sectionId,
    responseType: question?.responseTypeValue ?? DEFAULT_RESPONSE_TYPE,
    mandatory: question?.mandatory ?? true,
    displayOrder: question?.order ?? 0,
  };

  const [draft, setDraft] = useState<QuestionDraft>(original);
  const [questionCode, setQuestionCode] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  const editIntent = mode === 'add' ? 'version' : intentFor(draft, original);
  const isMove = editIntent === 'move';
  const token = question?.id ?? `add:${sectionId}`;

  function set<K extends keyof QuestionDraft>(key: K, value: QuestionDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setError(null);
  }

  async function onSave() {
    const wording = draft.wording.trim();
    if (!wording) {
      setError('Enter the question wording.');
      return;
    }

    if (mode === 'add' && !questionCode.trim()) {
      setError('Enter a question code. Codes are unique and are never reused.');
      return;
    }

    if (isMove && !questionCode.trim()) {
      setError('Enter a new code for the question in its new section. Codes are never reused.');
      return;
    }

    if (isMove && !reason.trim()) {
      setError('Say why the question is moving. The reason is recorded on the audit trail.');
      return;
    }

    if (mode === 'edit' && editIntent === 'none') {
      onClose();
      return;
    }

    setBusy(true);
    setError(null);

    const key = intent.keyFor(token);
    const result =
      mode === 'add'
        ? await addQuestion({
            sectionId: draft.sectionId,
            questionCode: questionCode.trim(),
            // al_name is a required short label; the wording is the only thing the
            // administrator has typed, so it doubles as the name within its 200-char limit.
            name: wording.slice(0, 200),
            wording,
            responseType: draft.responseType,
            mandatory: draft.mandatory,
            displayOrder: draft.displayOrder || undefined,
            idempotencyKey: key,
          })
        : isMove
          ? await moveQuestion({
              questionId: question!.id,
              targetSectionId: draft.sectionId,
              newQuestionCode: questionCode.trim(),
              reason: reason.trim(),
              idempotencyKey: key,
            })
          : await retireAndSucceedQuestion({
              questionId: question!.id,
              newWording: wording,
              responseType: draft.responseType,
              mandatory: draft.mandatory,
              displayOrder: draft.displayOrder,
              idempotencyKey: key,
            });

    setBusy(false);
    if (result.ok) {
      intent.release(token);
      onSaved();
      onClose();
    } else {
      setError(result.message);
    }
  }

  return (
    <Modal
      title={mode === 'add' ? 'Add a question' : `Edit ${question?.code ?? 'question'}`}
      onClose={onClose}
    >
      <div className="library__form">
        <label className="library__field">
          <span>Wording</span>
          <textarea
            value={draft.wording}
            onChange={(e) => set('wording', e.target.value)}
            rows={3}
          />
        </label>

        <label className="library__field">
          <span>Section</span>
          <select
            value={draft.sectionId}
            onChange={(e) => set('sectionId', e.target.value)}
            disabled={mode === 'add'}
          >
            {sections.map((section) => (
              <option key={section.id} value={section.id}>
                {section.name} ({section.ownerRole})
              </option>
            ))}
          </select>
        </label>

        <label className="library__field">
          <span>Response type</span>
          <select
            value={draft.responseType}
            onChange={(e) => set('responseType', Number(e.target.value))}
            disabled={isMove}
          >
            {RESPONSE_TYPE_OPTIONS.map((option) => (
              <option key={option.value} value={option.value}>
                {option.label}
              </option>
            ))}
          </select>
        </label>

        <label className="library__check">
          <input
            type="checkbox"
            checked={draft.mandatory}
            onChange={(e) => set('mandatory', e.target.checked)}
            disabled={isMove}
          />
          <span>Mandatory</span>
        </label>

        <label className="library__field">
          <span>Display order</span>
          <input
            type="number"
            value={draft.displayOrder}
            onChange={(e) => set('displayOrder', Number(e.target.value))}
            disabled={isMove}
            min={0}
          />
        </label>

        {mode === 'add' || isMove ? (
          <label className="library__field">
            <span>{isMove ? 'New question code' : 'Question code'}</span>
            <input
              type="text"
              value={questionCode}
              onChange={(e) => setQuestionCode(e.target.value)}
              placeholder="Q-E1-06"
            />
          </label>
        ) : null}

        {isMove ? (
          <label className="library__field">
            <span>Reason</span>
            <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} />
          </label>
        ) : null}

        <p className="library__consequence" role="status">
          {mode === 'add'
            ? 'The question is added to the checklist version in force. If it is mandatory it will be owed by every review of that team that has not yet been submitted.'
            : isMove
              ? 'Moving retires this question where it is and creates it in the new section under a new code, so the answers already given stay attached to the section they were answered in. The wording, response type and mandatory flag are carried across — other edits in this form are not applied.'
              : editIntent === 'none'
                ? 'Nothing has changed yet.'
                : 'Saving creates a new version and retires the current one. Reviews already submitted keep the version they were answered against.'}
        </p>

        {error ? (
          <p className="library__error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="library__form-actions">
          <button type="button" className="library__btn" onClick={onSave} disabled={busy}>
            {busy ? 'Saving…' : mode === 'add' ? 'Add question' : isMove ? 'Move question' : 'Save new version'}
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
