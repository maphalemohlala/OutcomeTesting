import { Modal } from '../../components/feedback/Modal';
import type { LibraryQuestion, LibrarySection } from './useQuestionLibrary';
import { useQuestionHistory, type HistoryState } from './useQuestionHistory';
import { useSectionHistory } from './useSectionHistory';

function formatWhen(iso: string | null): string {
  if (!iso) return 'Date not recorded';
  const time = new Date(iso).getTime();
  return Number.isNaN(time) ? 'Date not recorded' : new Date(time).toLocaleString('en-GB');
}

/**
 * What changed on one question, when, and by whom (2026-09-28). Read-only: the entries come
 * from the versions an edit leaves behind and the audit events the commands write, neither
 * of which this page can change.
 */
export function QuestionHistoryModal({
  question,
  onClose,
}: {
  question: LibraryQuestion;
  onClose: () => void;
}) {
  const state = useQuestionHistory(question.id);
  return (
    <HistoryModal title={`History — ${question.code}`} subject={question.wording} state={state} onClose={onClose} />
  );
}

/** The same for a section: its own additions, edits and retirement, not its questions'. */
export function SectionHistoryModal({
  section,
  onClose,
}: {
  section: LibrarySection;
  onClose: () => void;
}) {
  const state = useSectionHistory(section.id);
  return (
    <HistoryModal
      title={`History — ${section.code}`}
      subject={section.name}
      note="Changes to the section itself. Each question's own history is under its History button."
      state={state}
      onClose={onClose}
    />
  );
}

function HistoryModal({
  title,
  subject,
  note,
  state,
  onClose,
}: {
  title: string;
  subject: string;
  note?: string;
  state: HistoryState;
  onClose: () => void;
}) {
  return (
    <Modal title={title} onClose={onClose}>
      <p className="history__subject">{subject}</p>
      {note ? <p className="history__muted history__note">{note}</p> : null}

      {state.status === 'loading' ? <p role="status">Loading the history…</p> : null}
      {state.status === 'unavailable' ? <p role="alert">{state.reason}</p> : null}

      {state.status === 'ready' ? (
        state.entries.length === 0 ? (
          <p>No history is recorded.</p>
        ) : (
          <ol className="history">
            {state.entries.map((entry) => (
              <li key={entry.id} className="history__entry" data-action={entry.action}>
                <div className="history__head">
                  <strong>{entry.action}</strong>
                  {entry.version === null ? null : (
                    <span className="history__muted">v{entry.version}</span>
                  )}
                </div>
                <p className="history__who">
                  {entry.who ?? 'Unknown person'} · {formatWhen(entry.when)}
                </p>
                {entry.reason ? <p className="history__reason">“{entry.reason}”</p> : null}

                {entry.changes.length > 0 ? (
                  <dl className="history__changes">
                    {entry.changes.map((change) => (
                      <div key={change.field} className="history__change">
                        <dt>{change.field}</dt>
                        <dd>
                          <span className="history__before">
                            <span className="visually-hidden">Before: </span>
                            {change.before || '—'}
                          </span>
                          <span aria-hidden="true" className="history__arrow">
                            →
                          </span>
                          <span className="history__after">
                            <span className="visually-hidden">After: </span>
                            {change.after || '—'}
                          </span>
                        </dd>
                      </div>
                    ))}
                  </dl>
                ) : entry.action === 'Added' ? (
                  <p className="history__wording">{entry.wording}</p>
                ) : entry.action === 'Edited' ? (
                  <p className="history__muted">Saved with no change to the wording or settings.</p>
                ) : null}
              </li>
            ))}
          </ol>
        )
      ) : null}
    </Modal>
  );
}
