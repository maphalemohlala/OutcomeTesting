import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { useIntentKeys } from '../../hooks/useIntentKey';
import {
  addSection,
  updateSection,
  OWNER_ROLE_AQS,
  OWNER_ROLE_BOTH,
  OWNER_ROLE_TAX,
  type SectionQuestionInput,
} from '../../services/commands/sections';
import { RESPONSE_TYPE_OPTIONS, type LibrarySection } from './useQuestionLibrary';

/**
 * The section editor (AD-123). Add creates a section under the checklist version in force,
 * with its questions, in one transaction; edit amends it in place and records the before and
 * after of each changed field on an immutable Audit Event.
 */

interface Props {
  mode: 'add' | 'edit';
  section?: LibrarySection;
  onClose: () => void;
  onSaved: () => void;
}

const TEAMS = [
  { value: OWNER_ROLE_TAX, label: 'Tax' },
  { value: OWNER_ROLE_AQS, label: 'AQS' },
  { value: OWNER_ROLE_BOTH, label: 'Both' },
];

const DEFAULT_RESPONSE_TYPE = RESPONSE_TYPE_OPTIONS[0]?.value ?? 120910006;

type DraftQuestion = SectionQuestionInput & { key: number };

export function SectionModal({ mode, section, onClose, onSaved }: Props) {
  const [sectionCode, setSectionCode] = useState(section?.code ?? '');
  const [name, setName] = useState(section?.name ?? '');
  const [helpText, setHelpText] = useState(section?.helpText ?? '');
  const [ownerRole, setOwnerRole] = useState(section?.ownerRoleValue ?? OWNER_ROLE_AQS);
  const [displayOrder, setDisplayOrder] = useState(section?.order ?? 0);
  const [isOptional, setIsOptional] = useState(section?.isOptional ?? false);
  const [reason, setReason] = useState('');
  const [questions, setQuestions] = useState<DraftQuestion[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const intent = useIntentKeys();

  const token = section?.id ?? 'add-section';
  const teamChanged = mode === 'edit' && ownerRole !== section?.ownerRoleValue;
  const becomingOptional = mode === 'edit' && isOptional && !section?.isOptional;

  function addRow() {
    setQuestions((rows) => [
      ...rows,
      {
        key: Date.now() + rows.length,
        code: '',
        name: '',
        wording: '',
        responseType: DEFAULT_RESPONSE_TYPE,
        mandatory: true,
      },
    ]);
  }

  function setRow(key: number, patch: Partial<SectionQuestionInput>) {
    setQuestions((rows) => rows.map((row) => (row.key === key ? { ...row, ...patch } : row)));
    setError(null);
  }

  async function onSave() {
    if (!name.trim()) {
      setError('Enter the section name.');
      return;
    }

    if (mode === 'add' && !sectionCode.trim()) {
      setError('Enter a section code. Codes are unique and are never reused.');
      return;
    }

    if (mode === 'edit' && !reason.trim()) {
      setError('Say why the section is being amended. The reason is recorded on the audit trail.');
      return;
    }

    // Validated here as well as server-side, where the whole array is checked before
    // anything is written: a half-filled row should cost a keystroke, not a round trip.
    const incomplete = questions.find((row) => !row.code.trim() || !row.wording.trim());
    if (incomplete) {
      setError('Every question needs a code and its wording, or remove the row.');
      return;
    }

    setBusy(true);
    setError(null);

    const key = intent.keyFor(token);
    const result =
      mode === 'add'
        ? await addSection({
            sectionCode: sectionCode.trim(),
            name: name.trim(),
            helpText: helpText.trim() || undefined,
            ownerRole,
            displayOrder: displayOrder || undefined,
            isOptional,
            questions: questions.map((row) => ({
              code: row.code.trim(),
              // al_name is a required short label and the wording is the only text typed.
              name: row.wording.trim().slice(0, 200),
              wording: row.wording.trim(),
              responseType: row.responseType,
              mandatory: row.mandatory,
            })),
            idempotencyKey: key,
          })
        : await updateSection({
            sectionId: section!.id,
            name: name.trim(),
            helpText: helpText.trim(),
            ownerRole,
            displayOrder,
            isOptional,
            reason: reason.trim(),
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
    <Modal title={mode === 'add' ? 'Add a section' : `Edit ${section?.code ?? 'section'}`} onClose={onClose}>
      <div className="library__form">
        {mode === 'add' ? (
          <label className="library__field">
            <span>Section code</span>
            <input
              type="text"
              value={sectionCode}
              onChange={(e) => setSectionCode(e.target.value)}
              placeholder="S-CD2"
            />
          </label>
        ) : null}

        <label className="library__field">
          <span>Name</span>
          <input type="text" value={name} onChange={(e) => setName(e.target.value)} />
        </label>

        <label className="library__field">
          <span>Help text</span>
          <textarea value={helpText} onChange={(e) => setHelpText(e.target.value)} rows={2} />
        </label>

        <label className="library__field">
          <span>Team</span>
          <select value={ownerRole} onChange={(e) => setOwnerRole(Number(e.target.value))}>
            {TEAMS.map((team) => (
              <option key={team.value} value={team.value}>
                {team.label}
              </option>
            ))}
          </select>
        </label>

        {teamChanged ? (
          <p className="library__warning" role="status">
            Changing the team reaches reviews already submitted. The team a section belongs to
            is not versioned, so a section moved from Tax to AQS stops appearing in submitted
            Tax reviews and starts appearing in AQS ones that never answered it. The answers
            are not lost. <strong>Both</strong> adds a team without taking one away.
          </p>
        ) : null}

        <label className="library__field">
          <span>Display order</span>
          <input
            type="number"
            value={displayOrder}
            onChange={(e) => setDisplayOrder(Number(e.target.value))}
            min={0}
          />
        </label>

        <label className="library__check">
          <input
            type="checkbox"
            checked={isOptional}
            onChange={(e) => setIsOptional(e.target.checked)}
          />
          <span>Optional — its questions are not owed at submit</span>
        </label>

        {becomingOptional ? (
          <p className="library__warning" role="status">
            This takes effect on every review that has not been submitted, immediately. There
            is no date on the flag; retiring the section is the dated action.
          </p>
        ) : null}

        {mode === 'edit' ? (
          <label className="library__field">
            <span>Reason</span>
            <textarea value={reason} onChange={(e) => setReason(e.target.value)} rows={2} />
          </label>
        ) : null}

        {mode === 'add' ? (
          <fieldset className="library__questions-draft">
            <legend>Questions</legend>
            {questions.length === 0 ? (
              <p className="library__muted">
                None yet. A section can be created empty and filled in afterwards.
              </p>
            ) : null}

            {questions.map((row, index) => (
              <div key={row.key} className="library__draft-row">
                <span className="library__muted">{index + 1}</span>
                <input
                  type="text"
                  value={row.code}
                  onChange={(e) => setRow(row.key, { code: e.target.value })}
                  placeholder="Q-CD2-01"
                  aria-label={`Question ${index + 1} code`}
                />
                <input
                  type="text"
                  value={row.wording}
                  onChange={(e) => setRow(row.key, { wording: e.target.value })}
                  placeholder="Wording as the reviewer reads it"
                  aria-label={`Question ${index + 1} wording`}
                />
                <select
                  value={row.responseType}
                  onChange={(e) => setRow(row.key, { responseType: Number(e.target.value) })}
                  aria-label={`Question ${index + 1} response type`}
                >
                  {RESPONSE_TYPE_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
                <label className="library__check">
                  <input
                    type="checkbox"
                    checked={row.mandatory ?? true}
                    onChange={(e) => setRow(row.key, { mandatory: e.target.checked })}
                  />
                  <span>Mandatory</span>
                </label>
                <button
                  type="button"
                  className="library__btn library__btn--ghost"
                  onClick={() => setQuestions((rows) => rows.filter((r) => r.key !== row.key))}
                >
                  Remove
                </button>
              </div>
            ))}

            <button type="button" className="library__btn library__btn--ghost" onClick={addRow}>
              Add a question
            </button>
          </fieldset>
        ) : null}

        {error ? (
          <p className="library__error" role="alert">
            {error}
          </p>
        ) : null}

        <div className="library__form-actions">
          <button type="button" className="library__btn" onClick={onSave} disabled={busy}>
            {busy ? 'Saving…' : mode === 'add' ? 'Add section' : 'Save changes'}
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
