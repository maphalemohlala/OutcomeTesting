import { useState } from 'react';
import { ValidationSummary } from '../../components/feedback/ValidationSummary';
import { useIntentKeys } from '../../hooks/useIntentKey';
import { messageForFailure } from '../../services/errors';
import {
  IMPORT_RESOLUTIONS,
  IMPORT_RESOLUTION_HELP,
  resolveImportException,
  type ImportResolution,
} from '../../services/commands/resolveImportException';
import type { ExceptionSummary } from './useCaseIntake';

interface Props {
  exception: ExceptionSummary;
  onResolved: () => void;
  onCancel: () => void;
}

/**
 * Closes one import exception (BR-002, FR-002/FR-003) — the write half of the intake loop
 * that until now only rendered. The row goes back to whoever produced the extract with its
 * validation reason; this records what became of it.
 *
 * The note is mandatory here as well as in `al_ResolveImportException`, so a user is told
 * why up front rather than discovering it from a rejection. The screen is an affordance,
 * not a boundary: the command re-checks the caller server-side (NFR-SEC-01).
 */
export function ResolveExceptionForm({ exception, onResolved, onCancel }: Props) {
  const [resolution, setResolution] = useState<ImportResolution | ''>('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [failure, setFailure] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const intent = useIntentKeys();

  function onSubmit(event: React.FormEvent) {
    event.preventDefault();
    if (saving) return;

    const found: string[] = [];
    if (!resolution) found.push('Choose whether the row was resolved or ignored.');
    if (!note.trim()) found.push('Say what was done about the row. It is recorded permanently.');
    setErrors(found);
    if (found.length > 0 || !resolution) return;

    setFailure(null);
    setSaving(true);

    // One key per (exception, resolution) intent, so a retry after a timeout replays the
    // original closure instead of writing a second one (NFR-REL-01).
    const token = `resolve-exception:${exception.id}:${resolution}`;

    resolveImportException({
      exceptionId: exception.id,
      resolution,
      note: note.trim(),
      idempotencyKey: intent.keyFor(token),
    })
      .then((result) => {
        setSaving(false);
        if (!result.ok) {
          setFailure(messageForFailure(result));
          return;
        }
        intent.release(token);
        onResolved();
      })
      .catch(() => {
        setSaving(false);
        setFailure('We could not close this exception. Nothing has been changed.');
      });
  }

  const noteId = `resolve-note-${exception.id}`;

  return (
    <form className="intake__resolve" onSubmit={onSubmit}>
      <h3 className="intake__resolve-heading">
        Close row {exception.rowNumber ?? '—'}
        {exception.caseReference ? ` (${exception.caseReference})` : ''}
      </h3>

      <ValidationSummary errors={errors} />
      {failure ? (
        <p className="intake__notice intake__notice--error" role="alert">
          {failure}
        </p>
      ) : null}

      <fieldset className="intake__resolve-choices">
        <legend>What happened to this row?</legend>
        {IMPORT_RESOLUTIONS.map((option) => (
          <label key={option} className="intake__resolve-choice">
            <input
              type="radio"
              name={`resolution-${exception.id}`}
              value={option}
              checked={resolution === option}
              onChange={() => setResolution(option)}
            />
            <span>
              <strong>{option}</strong>
              <span className="intake__resolve-help">{IMPORT_RESOLUTION_HELP[option]}</span>
            </span>
          </label>
        ))}
      </fieldset>

      <label className="intake__resolve-field" htmlFor={noteId}>
        Note
        <textarea
          id={noteId}
          rows={3}
          value={note}
          onChange={(event) => setNote(event.target.value)}
          required
        />
      </label>

      <div className="intake__actions">
        <button type="submit" className="intake__btn" disabled={saving}>
          {saving ? 'Closing…' : 'Close exception'}
        </button>
        <button
          type="button"
          className="intake__btn intake__btn--ghost"
          onClick={onCancel}
          disabled={saving}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}
