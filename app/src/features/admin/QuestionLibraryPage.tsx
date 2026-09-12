import { useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { useQuestionLibrary, type LibraryQuestion, type LibrarySection } from './useQuestionLibrary';
import { usePermissions } from '../../app/permissions/permissionContext';
import { protectedReason } from './protectedQuestions';
import { QuestionModal } from './QuestionModal';
import { RetireModal } from './RetireModal';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { retireQuestion } from '../../services/commands/questions';
import './QuestionLibraryPage.css';

/** What the page currently has open, if anything. */
type Editing =
  | { kind: 'none' }
  | { kind: 'add-question'; sectionId: string }
  | { kind: 'edit-question'; sectionId: string; question: LibraryQuestion }
  | { kind: 'retire-question'; question: LibraryQuestion };

function QuestionRow({
  question,
  canEdit,
  onEdit,
  onRetire,
}: {
  question: LibraryQuestion;
  canEdit: boolean;
  onEdit: () => void;
  onRetire: () => void;
}) {
  const guarded = protectedReason(question.code);

  return (
    <li className="library__question" data-retired={question.retired ? 'true' : 'false'}>
      <p className="library__wording">{question.wording}</p>
      <div className="library__question-meta">
        <span className="library__code">{question.code}</span>
        <span className="library__response">{question.responseType}</span>
        <span className="library__requirement" data-required={question.mandatory ? 'true' : 'false'}>
          {question.mandatory ? 'Mandatory' : 'Optional'}
        </span>
        <span className="library__muted">v{question.versionNumber}</span>

        {guarded ? (
          <span className="library__flag" title={guarded}>
            Required by grading
          </span>
        ) : null}

        {/* A retired question offers no action: there is nothing to do to it, and offering
            something that would be refused is worse than offering nothing. */}
        {canEdit && !question.retired ? (
          <button type="button" className="library__btn library__btn--ghost" onClick={onEdit}>
            Edit question
          </button>
        ) : null}

        {/* Retire is hidden for a protected code because the server refuses it (AD-122).
            The chip above already says which question it is and why. */}
        {canEdit && !question.retired && !guarded ? (
          <button type="button" className="library__btn library__btn--ghost" onClick={onRetire}>
            Retire
          </button>
        ) : null}
      </div>
    </li>
  );
}

function SectionBlock({
  section,
  canEdit,
  onAddQuestion,
  onEditQuestion,
  onRetireQuestion,
}: {
  section: LibrarySection;
  canEdit: boolean;
  onAddQuestion: () => void;
  onEditQuestion: (question: LibraryQuestion) => void;
  onRetireQuestion: (question: LibraryQuestion) => void;
}) {
  const live = section.questions.filter((question) => !question.retired);
  const retired = section.questions.filter((question) => question.retired);

  return (
    <section className="library__section" aria-labelledby={`section-${section.id}`}>
      <header className="library__section-head">
        <h2 id={`section-${section.id}`}>{section.name}</h2>
        <div className="library__section-meta">
          <span className="library__owner">{section.ownerRole}</span>
          {section.isOptional ? <span className="library__flag">Optional</span> : null}
          {section.conditional ? <span className="library__flag">Conditional</span> : null}
          <span className="library__muted">
            {live.length} {live.length === 1 ? 'question' : 'questions'}
          </span>
          {canEdit && !section.retired ? (
            <button type="button" className="library__btn library__btn--ghost" onClick={onAddQuestion}>
              Add question
            </button>
          ) : null}
        </div>
      </header>

      {live.length === 0 ? (
        <p className="library__muted">No questions in this section.</p>
      ) : (
        <ol className="library__questions">
          {live.map((question) => (
            <QuestionRow
              key={question.id}
              question={question}
              canEdit={canEdit}
              onEdit={() => onEditQuestion(question)}
              onRetire={() => onRetireQuestion(question)}
            />
          ))}
        </ol>
      )}

      {retired.length > 0 ? (
        <details className="library__retired">
          <summary>Retired ({retired.length})</summary>
          <ol className="library__questions">
            {retired.map((question) => (
              <QuestionRow
                key={question.id}
                question={question}
                canEdit={canEdit}
                onEdit={() => onEditQuestion(question)}
                onRetire={() => onRetireQuestion(question)}
              />
            ))}
          </ol>
        </details>
      ) : null}
    </section>
  );
}

export function QuestionLibraryPage() {
  const [reloadKey, setReloadKey] = useState(0);
  const [editing, setEditing] = useState<Editing>({ kind: 'none' });
  const state = useQuestionLibrary(reloadKey);
  const { can, ready } = usePermissions();
  const canEdit = ready && can('question.retire', 'Edit');

  const sections = state.status === 'ready' ? state.sections : [];
  const live = sections.filter((section) => !section.retired);
  const retired = sections.filter((section) => section.retired);
  const reload = () => setReloadKey((key) => key + 1);
  const intent = useIntentKeys();
  const close = () => setEditing({ kind: 'none' });

  return (
    <>
      <PageIntro
        title="Question library"
        purpose="The checklist as reviewers answer it today. Editing creates a new version and retires the old one, so answers already given keep the version they were answered against (FR-030, FR-031)."
      />

      {state.status === 'loading' ? <p role="status">Loading the question library…</p> : null}

      {state.status === 'unavailable' ? (
        <section className="library__unavailable" aria-labelledby="library-unavailable">
          <h2 id="library-unavailable">The library cannot be shown</h2>
          <p>{state.reason}</p>
        </section>
      ) : null}

      {state.status === 'ready' ? (
        sections.length === 0 ? (
          <section className="library__unavailable" aria-labelledby="library-empty">
            <h2 id="library-empty">No checklist is published</h2>
            <p>No sections are visible to you yet.</p>
          </section>
        ) : (
          <div className="library">
            {live.map((section) => (
              <SectionBlock
                key={section.id}
                section={section}
                canEdit={canEdit}
                onAddQuestion={() => setEditing({ kind: 'add-question', sectionId: section.id })}
                onEditQuestion={(question) =>
                  setEditing({ kind: 'edit-question', sectionId: section.id, question })
                }
                onRetireQuestion={(question) => setEditing({ kind: 'retire-question', question })}
              />
            ))}

            {retired.length > 0 ? (
              <details className="library__retired library__retired--sections">
                <summary>Retired sections ({retired.length})</summary>
                {retired.map((section) => (
                  <SectionBlock
                    key={section.id}
                    section={section}
                    canEdit={false}
                    onAddQuestion={() => undefined}
                    onEditQuestion={() => undefined}
                    onRetireQuestion={() => undefined}
                  />
                ))}
              </details>
            ) : null}
          </div>
        )
      ) : null}

      {editing.kind === 'retire-question' ? (
        <RetireModal
          subject="question"
          name={`${editing.question.code} — ${editing.question.wording}`}
          onConfirm={(reason, effectiveTo) =>
            retireQuestion({
              questionId: editing.question.id,
              reason,
              effectiveTo,
              idempotencyKey: intent.keyFor(`retire:${editing.question.id}`),
            })
          }
          onClose={close}
          onRetired={() => {
            intent.release(`retire:${editing.question.id}`);
            reload();
          }}
        />
      ) : null}

      {editing.kind === 'add-question' || editing.kind === 'edit-question' ? (
        <QuestionModal
          mode={editing.kind === 'add-question' ? 'add' : 'edit'}
          sections={live}
          sectionId={editing.sectionId}
          question={editing.kind === 'edit-question' ? editing.question : undefined}
          onClose={close}
          onSaved={reload}
        />
      ) : null}
    </>
  );
}
