import { useRef, useState } from 'react';
import { PageIntro } from '../../components/layout/PageIntro';
import { useQuestionLibrary, type LibraryQuestion, type LibrarySection } from './useQuestionLibrary';
import { usePermissions } from '../../app/permissions/permissionContext';
import { protectedReason } from './protectedQuestions';
import { QuestionHistoryModal, SectionHistoryModal } from './HistoryModal';
import { QuestionModal } from './QuestionModal';
import { RetireModal } from './RetireModal';
import { FailPointsPanel } from './FailPointsPanel';
import { SectionModal } from './SectionModal';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { retireQuestion } from '../../services/commands/questions';
import { retireSection } from '../../services/commands/sections';
import { Icon } from '../../components/icons/Icon';
import { splitRemediationAreas, type RemediationArea } from './remediationAreas';
import './QuestionLibraryPage.css';
import { Skeleton } from '../../components/feedback/Skeleton';

/** What the page currently has open, if anything. */
type Editing =
  | { kind: 'none' }
  | { kind: 'add-question'; sectionId: string }
  | { kind: 'edit-question'; sectionId: string; question: LibraryQuestion }
  | { kind: 'retire-question'; question: LibraryQuestion }
  | { kind: 'question-history'; question: LibraryQuestion }
  | { kind: 'section-history'; section: LibrarySection }
  | { kind: 'add-section' }
  | { kind: 'edit-section'; section: LibrarySection }
  | { kind: 'retire-section'; section: LibrarySection };

/** Edit, history and the more menu, shared by an ordinary row and a remediation area's cell. */
function QuestionActions({
  question,
  canEdit,
  compact,
  onEdit,
  onRetire,
  onHistory,
}: {
  question: LibraryQuestion;
  canEdit: boolean;
  /** Icon-only Edit, for the narrow cells of a remediation area. */
  compact?: boolean;
  onEdit: () => void;
  onRetire: () => void;
  onHistory: () => void;
}) {
  const guarded = protectedReason(question.code);
  const live = !question.retired && !question.scheduled && !question.movedTo;
  const canRetire = canEdit && live && !guarded;

  return (
    <span className="library__actions">
      {/* A retired question offers no action: there is nothing to do to it, and offering
          something that would be refused is worse than offering nothing. Nor does one with
          a dated change to come: the server edits the newest version, not the one shown.
          A planned retirement alone hides nothing: an edit dated before it keeps it, and
          Retire can move it. A moved question offers neither: the server refuses both, and
          the chip says where to make the change. */}
      {canEdit && live ? (
        <button
          type="button"
          className={compact ? 'library__icon-btn' : 'button-secondary'}
          aria-label={`Edit question: ${question.wording}`}
          onClick={onEdit}
        >
          <Icon name="pencil" />
          {compact ? null : <span aria-hidden="true">Edit</span>}
        </button>
      ) : null}

      {/* Offered to everyone who can see the library, retired questions included: a
          retired question's history is exactly what somebody asks about afterwards. */}
      <button
        type="button"
        className="library__icon-btn"
        aria-label={`History of ${question.wording}`}
        title="History"
        onClick={onHistory}
      >
        <Icon name="history" />
      </button>

      {/* Retire is hidden for a protected code because the server refuses it; the lock
          beside the row says which question it is and why. */}
      {canRetire ? (
        <details className="library__more">
          <summary className="library__icon-btn" aria-label={`More actions for ${question.wording}`}>
            <Icon name="more" />
          </summary>
          <div className="library__menu">
            <button type="button" className="button-quiet button-quiet--danger" onClick={onRetire}>
              Retire
            </button>
          </div>
        </details>
      ) : compact ? null : (
        // Holds the menu's place, so Edit and History line up down the section.
        <span className="library__more-slot" aria-hidden="true" />
      )}
    </span>
  );
}

/** The states a row has to say in words; the dimming and line-through are invisible to a screen reader. */
function QuestionFlags({ question }: { question: LibraryQuestion }) {
  // The server's refusal, word for word but for naming both actions it refuses.
  const moved = question.movedTo
    ? `That question has already been moved to ${question.movedTo}, so it cannot be edited or retired. Make the change to ${question.movedTo} instead.`
    : null;
  if (!question.retired && !question.scheduled && !question.retires && !moved) return null;
  return (
    <span className="library__flags">
      {question.retired ? <span className="library__flag">Retired</span> : null}
      {question.scheduled ? <span className="library__flag">{question.scheduled}</span> : null}
      {question.retires ? <span className="library__flag">{question.retires}</span> : null}
      {moved ? (
        <span className="library__flag" title={moved}>
          Moved to {question.movedTo}
        </span>
      ) : null}
    </span>
  );
}

function Requirement({ mandatory }: { mandatory: boolean }) {
  return (
    <span className="library__requirement" data-required={mandatory ? 'true' : 'false'}>
      {mandatory ? 'Mandatory' : 'Optional'}
    </span>
  );
}

export function QuestionRow({
  question,
  canEdit,
  onEdit,
  onRetire,
  onHistory,
}: {
  question: LibraryQuestion;
  canEdit: boolean;
  onEdit: () => void;
  onRetire: () => void;
  onHistory: () => void;
}) {
  const guarded = protectedReason(question.code);

  return (
    <li className="library__question library__question--grid" data-retired={question.retired ? 'true' : 'false'}>
      <div className="library__question-text">
        <p className="library__wording">{question.wording}</p>
        <span className="library__code">{question.code}</span>
        <QuestionFlags question={question} />
      </div>
      <span>
        <span className="library__response">{question.responseType}</span>
      </span>
      <Requirement mandatory={question.mandatory} />
      <span className="library__muted">v{question.versionNumber}</span>
      <span>
        {guarded ? (
          <span className="library__system" title={guarded}>
            <Icon name="lock" />
            Read by the system
          </span>
        ) : null}
      </span>
      <QuestionActions
        question={question}
        canEdit={canEdit}
        onEdit={onEdit}
        onRetire={onRetire}
        onHistory={onHistory}
      />
    </li>
  );
}

/** One half of a remediation area: code, type, requirement and its actions. */
function AreaCell({
  question,
  canEdit,
  onEdit,
  onRetire,
  onHistory,
}: {
  question: LibraryQuestion;
  canEdit: boolean;
  onEdit: () => void;
  onRetire: () => void;
  onHistory: () => void;
}) {
  const guarded = protectedReason(question.code);
  return (
    <td>
      <span className="library__area-cell">
        <span className="library__code" title={guarded ?? undefined}>
          {question.code}
        </span>
        <span className="library__response">{question.responseType}</span>
        <Requirement mandatory={question.mandatory} />
        <QuestionActions
          question={question}
          canEdit={canEdit}
          compact
          onEdit={onEdit}
          onRetire={onRetire}
          onHistory={onHistory}
        />
      </span>
    </td>
  );
}

const SMALL_NUMBERS = ['', '', 'Both', 'All three', 'All four', 'All five', 'All six', 'All seven', 'All eight'];

/** Says what every area shares, so the table need not repeat it on each row. */
function areaSummary(areas: RemediationArea<LibraryQuestion>[]): string {
  const questions = areas.flatMap((a) => [a.required, a.comments]);
  const versions = new Set(questions.map((q) => q.versionNumber));
  const shared = [
    versions.size === 1 ? `version ${[...versions][0]}` : null,
    questions.every((q) => protectedReason(q.code)) ? 'read by the system' : null,
  ].filter(Boolean);
  const all = SMALL_NUMBERS[questions.length] ?? `All ${questions.length}`;
  return (
    'Each area asks whether remediation is required, then takes comments.' +
    (shared.length ? ` ${all} are ${shared.join(' and ')}.` : '')
  );
}

function SectionBlock({
  section,
  canEdit,
  onAddQuestion,
  onEditQuestion,
  onRetireQuestion,
  onQuestionHistory,
  onEditSection,
  onRetireSection,
  onSectionHistory,
}: {
  section: LibrarySection;
  canEdit: boolean;
  onAddQuestion: () => void;
  onEditQuestion: (question: LibraryQuestion) => void;
  onRetireQuestion: (question: LibraryQuestion) => void;
  onQuestionHistory: (question: LibraryQuestion) => void;
  onEditSection: () => void;
  onRetireSection: () => void;
  onSectionHistory: () => void;
}) {
  const live = section.questions.filter((question) => !question.retired);
  const retired = section.questions.filter((question) => question.retired);
  const { rows, areas } = splitRemediationAreas(live);
  const handlers = (question: LibraryQuestion) => ({
    onEdit: () => onEditQuestion(question),
    onRetire: () => onRetireQuestion(question),
    onHistory: () => onQuestionHistory(question),
  });

  return (
    <section className="library__section" aria-labelledby={`section-${section.id}`}>
      <header className="library__section-head">
        <div className="library__section-title">
          <h2 id={`section-${section.id}`}>{section.name}</h2>
          <span className="library__flag">{section.ownerRole}</span>
          {section.retired ? <span className="library__flag library__flag--muted">Retired</span> : null}
          {section.isOptional ? <span className="library__flag library__flag--muted">Optional</span> : null}
          {section.conditional ? <span className="library__flag library__flag--warn">Conditional</span> : null}
          <span className="library__muted">
            {live.length} {live.length === 1 ? 'question' : 'questions'}
          </span>
        </div>
        <div className="library__section-actions">
          {canEdit && !section.retired ? (
            <>
              <button type="button" className="button-primary" onClick={onAddQuestion}>
                <Icon name="plus" />
                Add question
              </button>
              <button type="button" className="button-secondary" onClick={onEditSection}>
                Edit section
              </button>
            </>
          ) : null}
          {/* Everyone who can read the library, retired sections included, as for questions. */}
          <button
            type="button"
            className="button-secondary"
            aria-label={`History of the ${section.name} section`}
            onClick={onSectionHistory}
          >
            <Icon name="history" />
            <span aria-hidden="true">History</span>
          </button>
          {canEdit && !section.retired ? (
            <button type="button" className="button-quiet button-quiet--danger" onClick={onRetireSection}>
              Retire section
            </button>
          ) : null}
        </div>
      </header>

      {live.length === 0 ? (
        <p className="library__muted library__empty">No questions in this section.</p>
      ) : rows.length > 0 ? (
        <ol className="library__questions">
          {rows.map((question) => (
            <QuestionRow key={question.id} question={question} canEdit={canEdit} {...handlers(question)} />
          ))}
        </ol>
      ) : null}

      {areas.length > 0 ? (
        <div className="library__areas">
          <p className="library__areas-intro">
            <strong>Remediation areas</strong> <span className="library__muted">{areaSummary(areas)}</span>
          </p>
          <div className="library__areas-scroll">
            <table className="library__areas-table">
              <thead>
                <tr>
                  <th scope="col">Area</th>
                  <th scope="col">Remediation required?</th>
                  <th scope="col">Remediation comments</th>
                </tr>
              </thead>
              <tbody>
                {areas.map((a) => (
                  <tr key={a.required.id}>
                    <th scope="row">{a.area}</th>
                    <AreaCell question={a.required} canEdit={canEdit} {...handlers(a.required)} />
                    <AreaCell question={a.comments} canEdit={canEdit} {...handlers(a.comments)} />
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}

      {retired.length > 0 ? (
        <details className="library__retired">
          <summary>
            Retired questions <span className="library__count">{retired.length}</span>
          </summary>
          <ol className="library__questions">
            {retired.map((question) => (
              <QuestionRow key={question.id} question={question} canEdit={canEdit} {...handlers(question)} />
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

  // One render path for live and retired sections. A retired block used to be handed five
  // no-op handlers to satisfy the props it could never fire; the block itself already
  // withholds every button once the section is retired.
  const renderSection = (section: LibrarySection) => (
    <SectionBlock
      key={section.id}
      section={section}
      canEdit={canEdit && !section.retired}
      onAddQuestion={() => setEditing({ kind: 'add-question', sectionId: section.id })}
      onEditQuestion={(question) =>
        setEditing({ kind: 'edit-question', sectionId: section.id, question })
      }
      onRetireQuestion={(question) => setEditing({ kind: 'retire-question', question })}
      onQuestionHistory={(question) => setEditing({ kind: 'question-history', question })}
      onEditSection={() => setEditing({ kind: 'edit-section', section })}
      onRetireSection={() => setEditing({ kind: 'retire-section', section })}
      onSectionHistory={() => setEditing({ kind: 'section-history', section })}
    />
  );

  const sections = state.status === 'ready' ? state.sections : [];
  const live = sections.filter((section) => !section.retired);
  const retired = sections.filter((section) => section.retired);
  const readBySystem = live
    .flatMap((section) => section.questions)
    .filter((question) => !question.retired && protectedReason(question.code)).length;
  const reload = () => setReloadKey((key) => key + 1);
  // Here rather than in each dialog, so a key outlives Cancel: a save whose answer was lost
  // and is sent again from a reopened dialog then replays instead of writing twice.
  const intent = useIntentKeys();
  const lastRetire = useRef('');

  function retireToken(target: string, reason: string, effectiveTo: unknown): string {
    lastRetire.current = `retire:${target}:${reason}:${String(effectiveTo)}`;
    return lastRetire.current;
  }
  const close = () => setEditing({ kind: 'none' });

  return (
    <>
      <PageIntro
        title="Question library"
        purpose="The questions checkers answer on each review. Editing a question makes a new version; cases already reviewed keep the version they used."
        actions={
          canEdit && state.status === 'ready' ? (
            <button type="button" className="button-primary" onClick={() => setEditing({ kind: 'add-section' })}>
              <Icon name="plus" />
              Add section
            </button>
          ) : undefined
        }
      />

      {state.status === 'loading' ? <Skeleton label="Loading the question library…" rows={6} /> : null}

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
            {readBySystem > 0 ? (
              <p className="library__info">
                <Icon name="lock" />
                <span>
                  The system reads the answers to {readBySystem} of these questions to set the outcome and raise
                  remediation. You can reword them, but they can’t be retired.
                </span>
              </p>
            ) : null}

            {live.map(renderSection)}

            <FailPointsPanel canEdit={canEdit} />

            {retired.length > 0 ? (
              <details className="library__retired library__retired--sections">
                <summary>
                  Retired sections <span className="library__count">{retired.length}</span>
                </summary>
                {retired.map(renderSection)}
              </details>
            ) : null}
          </div>
        )
      ) : null}

      {editing.kind === 'add-section' || editing.kind === 'edit-section' ? (
        <SectionModal
          intent={intent}
          mode={editing.kind === 'add-section' ? 'add' : 'edit'}
          section={editing.kind === 'edit-section' ? editing.section : undefined}
          onClose={close}
          onSaved={reload}
        />
      ) : null}

      {editing.kind === 'retire-section' ? (
        <RetireModal
          subject="section"
          name={`${editing.section.code} — ${editing.section.name}`}
          questionCount={editing.section.questions.filter((q) => !q.retired).length}
          onConfirm={(reason, effectiveTo) =>
            retireSection({
              sectionId: editing.section.id,
              reason,
              effectiveTo,
              idempotencyKey: intent.keyFor(retireToken(`section:${editing.section.id}`, reason, effectiveTo)),
            })
          }
          onClose={close}
          onRetired={() => {
            intent.release(lastRetire.current);
            reload();
          }}
        />
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
              idempotencyKey: intent.keyFor(retireToken(`question:${editing.question.id}`, reason, effectiveTo)),
            })
          }
          onClose={close}
          onRetired={() => {
            intent.release(lastRetire.current);
            reload();
          }}
        />
      ) : null}

      {editing.kind === 'question-history' ? (
        <QuestionHistoryModal question={editing.question} onClose={close} />
      ) : null}

      {editing.kind === 'section-history' ? (
        <SectionHistoryModal section={editing.section} onClose={close} />
      ) : null}

      {editing.kind === 'add-question' || editing.kind === 'edit-question' ? (
        <QuestionModal
          intent={intent}
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
