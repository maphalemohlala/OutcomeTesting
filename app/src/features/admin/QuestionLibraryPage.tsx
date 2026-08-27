import { useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import {
  useQuestionLibrary,
  RESPONSE_TYPE_OPTIONS,
  type LibraryQuestion,
} from './useQuestionLibrary';
import { usePermissions } from '../../app/permissions/permissionContext';
import {
  newQuestionIntentKey,
  retireAndSucceedQuestion,
} from '../../services/commands/questions';
import './QuestionLibraryPage.css';

function QuestionRow({
  question,
  canEdit,
  onSaved,
}: {
  question: LibraryQuestion;
  canEdit: boolean;
  onSaved: () => void;
}) {
  const [editing, setEditing] = useState(false);
  const [draft, setDraft] = useState(question.wording);
  const [draftResponseType, setDraftResponseType] = useState(question.responseTypeValue);
  const [draftMandatory, setDraftMandatory] = useState(question.mandatory);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSave() {
    if (!draft.trim()) {
      setError('Enter the new wording.');
      return;
    }
    setBusy(true);
    setError(null);
    const result = await retireAndSucceedQuestion({
      questionId: question.id,
      newWording: draft.trim(),
      responseType: draftResponseType,
      mandatory: draftMandatory,
      idempotencyKey: newQuestionIntentKey(),
    });
    setBusy(false);
    if (result.ok) {
      setEditing(false);
      onSaved();
    } else {
      setError(result.message);
    }
  }

  return (
    <li className="library__question">
      {editing ? (
        <div className="library__edit">
          <textarea
            className="library__textarea"
            value={draft}
            onChange={(e) => setDraft(e.target.value)}
            rows={3}
            aria-label="New question wording"
          />
          <div className="library__edit-fields">
            <label className="library__field">
              <span>Response type</span>
              <select
                value={draftResponseType}
                onChange={(e) => setDraftResponseType(Number(e.target.value))}
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
                checked={draftMandatory}
                onChange={(e) => setDraftMandatory(e.target.checked)}
              />
              <span>Mandatory</span>
            </label>
          </div>
          <div className="library__edit-actions">
            <button type="button" className="library__btn" onClick={onSave} disabled={busy}>
              {busy ? 'Saving…' : 'Save new version'}
            </button>
            <button
              type="button"
              className="library__btn library__btn--ghost"
              onClick={() => {
                setEditing(false);
                setDraft(question.wording);
                setDraftResponseType(question.responseTypeValue);
                setDraftMandatory(question.mandatory);
                setError(null);
              }}
              disabled={busy}
            >
              Cancel
            </button>
          </div>
          {error ? (
            <p className="library__error" role="status">
              {error}
            </p>
          ) : null}
        </div>
      ) : (
        <>
          <p className="library__wording">{question.wording}</p>
          <div className="library__question-meta">
            <span className="library__response">{question.responseType}</span>
            <span
              className="library__requirement"
              data-required={question.mandatory ? 'true' : 'false'}
            >
              {question.mandatory ? 'Mandatory' : 'Optional'}
            </span>
            <span className="library__muted">v{question.versionNumber}</span>
            {canEdit ? (
              <button
                type="button"
                className="library__btn library__btn--ghost"
                onClick={() => setEditing(true)}
              >
                Edit question
              </button>
            ) : null}
          </div>
        </>
      )}
    </li>
  );
}

export function QuestionLibraryPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useQuestionLibrary(reloadKey);
  const { can } = usePermissions();
  const canEdit = can('question.retire', 'Edit');

  return (
    <>
      <PageIntro
        title="Question library"
        purpose="See the published checklist as reviewers answer it today. Editing creates a new version and retires the old one, so historic responses are preserved (FR-030, FR-031)."
      />

      {state.status === 'loading' ? <p role="status">Loading the question library…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="library__unavailable" aria-labelledby="library-unavailable">
          <h2 id="library-unavailable">The library cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        state.sections.length === 0 ? (
          <section className="library__unavailable" aria-labelledby="library-empty">
            <h2 id="library-empty">No checklist is published</h2>
            <p>No sections are visible to you yet.</p>
          </section>
        ) : (
          <div className="library">
            {state.sections.map((section) => (
              <section
                key={section.id}
                className="library__section"
                aria-labelledby={`section-${section.id}`}
              >
                <header className="library__section-head">
                  <h2 id={`section-${section.id}`}>{section.name}</h2>
                  <div className="library__section-meta">
                    <span className="library__owner">{section.ownerRole}</span>
                    {section.conditional ? (
                      <span className="library__flag">Conditional</span>
                    ) : null}
                    <span className="library__muted">
                      {section.questions.length}{' '}
                      {section.questions.length === 1 ? 'question' : 'questions'}
                    </span>
                  </div>
                </header>

                {section.questions.length === 0 ? (
                  <p className="library__muted">No questions in this section.</p>
                ) : (
                  <ol className="library__questions">
                    {section.questions.map((question) => (
                      <QuestionRow
                        key={question.id}
                        question={question}
                        canEdit={canEdit}
                        onSaved={() => setReloadKey((k) => k + 1)}
                      />
                    ))}
                  </ol>
                )}
              </section>
            ))}
          </div>
        )
      ) : null}
    </>
  );
}
