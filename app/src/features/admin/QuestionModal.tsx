import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { addQuestion, moveQuestion, retireAndSucceedQuestion } from '../../services/commands/questions';
import { today } from './effectiveDay';
import { intentFor, refusalFor, type QuestionDraft } from './questionModalIntent';
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

export function QuestionModal({ mode, sections, sectionId, question, onClose, onSaved }: Props) {
  const original: QuestionDraft = {
    wording: question?.wording ?? '',
    sectionId,
    responseType: question?.responseTypeValue ?? null,
    mandatory: question?.mandatory ?? true,
    displayOrder: question?.order ?? 0,
  };

  const [draft, setDraft] = useState<QuestionDraft>(original);
  const [questionCode, setQuestionCode] = useState('');
  // The day the change starts being asked. AD-122 accepts that a mandatory question in
  // force today is owed by every unsubmitted review of that discipline at its next submit,
  // and names this as the control for it - dating it forward lets reviews already open
  // finish against the set they started with. Today by default, which is what every one of
  // these commands did before the field existed.
  const [effectiveFrom, setEffectiveFrom] = useState(today());
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  const editIntent = mode === 'add' ? 'version' : intentFor(draft, original);
  const isMove = editIntent === 'move';
  const token = question?.id ?? `add:${sectionId}`;

  // A move carries the wording, response type, mandatory flag and display order forward from
  // the version it retires, so those four controls show the values that will actually be
  // carried, disabled — not edits made before the section was changed, which are discarded.
  const shown = isMove ? original : draft;

  function set<K extends keyof QuestionDraft>(key: K, value: QuestionDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setError(null);
  }

  async function onSave() {
    const wording = draft.wording.trim();
    const refusal = refusalFor({
      mode,
      intent: editIntent,
      draft,
      questionCode,
      reason,
    });
    if (refusal) {
      setError(refusal);
      return;
    }

    if (mode === 'edit' && editIntent === 'none') {
      onClose();
      return;
    }

    if (!effectiveFrom) {
      setError('Choose the day the change takes effect.');
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
            responseType: draft.responseType!,
            mandatory: draft.mandatory,
            displayOrder: draft.displayOrder || undefined,
            effectiveFrom,
            idempotencyKey: key,
          })
        : isMove
          ? await moveQuestion({
              questionId: question!.id,
              targetSectionId: draft.sectionId,
              newQuestionCode: questionCode.trim(),
              reason: reason.trim(),
              effectiveFrom,
              idempotencyKey: key,
            })
          : await retireAndSucceedQuestion({
              questionId: question!.id,
              newWording: wording,
              responseType: draft.responseType!,
              mandatory: draft.mandatory,
              displayOrder: draft.displayOrder,
              effectiveFrom,
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
            value={shown.wording}
            onChange={(e) => set('wording', e.target.value)}
            disabled={isMove}
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
            value={shown.responseType ?? ''}
            onChange={(e) =>
              set('responseType', e.target.value === '' ? null : Number(e.target.value))
            }
            disabled={isMove}
          >
            {/* No option is selected until the administrator picks one. Left out, the
                browser selects the first — Text — and a question nobody meant to make
                free-text is created without anyone touching the control. */}
            <option value="" disabled>
              Select how it is answered…
            </option>
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
            checked={shown.mandatory}
            onChange={(e) => set('mandatory', e.target.checked)}
            disabled={isMove}
          />
          <span>Mandatory</span>
        </label>

        <label className="library__field">
          <span>Display order</span>
          <input
            type="number"
            value={shown.displayOrder}
            onChange={(e) => set('displayOrder', Number(e.target.value))}
            disabled={isMove}
            min={0}
          />
        </label>

        <label className="library__field">
          <span>
            {mode === 'add'
              ? 'Asked from'
              : isMove
                ? 'Asked in the new section from'
                : 'New wording asked from'}
          </span>
          <input
            type="date"
            value={effectiveFrom}
            min={today()}
            onChange={(e) => {
              setEffectiveFrom(e.target.value);
              setError(null);
            }}
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
            ? `The question is added to the checklist version in force${
                effectiveFrom === today() ? ' today' : ` on ${effectiveFrom}`
              }. If it is mandatory it will be owed by every review of that team that has not yet been submitted on that day.`
            : isMove
              ? 'Moving retires this question where it is and creates it in the new section under a new code, so the answers already given stay attached to the section they were answered in. The wording, response type, mandatory flag and display order are carried across unchanged, which is why they are shown here but cannot be edited — change them first, then move, or move first and then edit.'
              : editIntent === 'none'
                ? 'Nothing has changed yet.'
                : `Saving creates a new version${
                    effectiveFrom === today() ? '' : ` from ${effectiveFrom}`
                  } and retires the current one. Reviews already submitted keep the version they were answered against.`}
        </p>

        {error ? (
          <p className="library__error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="library__form-actions">
          <button
            type="button"
            className="library__btn library__btn--ghost"
            onClick={onClose}
            disabled={busy}
          >
            Cancel
          </button>
          <button type="button" className="library__btn" onClick={onSave} disabled={busy}>
            {busy ? 'Saving…' : mode === 'add' ? 'Add question' : isMove ? 'Move question' : 'Save new version'}
          </button>
        </div>
      </div>
    </Modal>
  );
}
