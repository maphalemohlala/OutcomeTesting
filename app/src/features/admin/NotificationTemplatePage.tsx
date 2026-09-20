import { useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { unknownTokens } from './notificationTemplates';
import {
  useNotificationTemplates,
  saveTemplate,
  type TemplateRow,
} from './useNotificationTemplates';
import './NotificationTemplatePage.css';

/**
 * The wording of every letter the solution sends (Change 1, AD-163).
 *
 * The page lists every letter, not every stored row. A letter nobody has edited is still
 * sent, on the wording compiled into the plug-in assembly, and showing only the rows would
 * make a partial list look like the whole set.
 *
 * Validation here is advisory and says so. `NotificationTemplateGuardPlugin` refuses the
 * same things server-side and its sentence is what the page shows on a failed save, because
 * the table is reachable from the Web API where this screen is not involved at all.
 */
export function NotificationTemplatePage() {
  const [reloadKey, setReloadKey] = useState(0);
  const state = useNotificationTemplates(reloadKey);
  const { can } = usePermissions();
  const canEdit = can('page.admin.templates', 'Manage');

  const [editing, setEditing] = useState<TemplateRow | null>(null);

  return (
    <div className="templates">
      <PageIntro
        title="Notification wording"
        purpose={
          'The subject and body of every email this system sends. A letter you have not ' +
          'edited uses the built-in wording, and changing one takes effect on the next send.'
        }
      />

      {state.status === 'loading' && <p className="templates__note">Loading…</p>}
      {state.status === 'unavailable' && <p className="templates__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <table className="templates__table">
          <thead>
            <tr>
              <th scope="col">Letter</th>
              <th scope="col">Wording</th>
              <th scope="col">Subject</th>
              {canEdit && (
                <th scope="col">
                  <span className="templates__sr">Actions</span>
                </th>
              )}
            </tr>
          </thead>
          <tbody>
            {state.templates.map((row) => (
              <tr key={row.code}>
                <td>
                  {row.name}
                  <span className="templates__code">{row.code}</span>
                </td>
                <td>
                  {row.stored ? (
                    'Edited'
                  ) : (
                    <span className="templates__builtin">Built-in</span>
                  )}
                </td>
                <td className="templates__subject">
                  {row.stored ? row.subject : <span className="templates__builtin">—</span>}
                </td>
                {canEdit && (
                  <td>
                    <button
                      type="button"
                      className="templates__btn templates__btn--ghost"
                      onClick={() => setEditing(row)}
                    >
                      Edit
                    </button>
                  </td>
                )}
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {editing && (
        <TemplateForm
          row={editing}
          onClose={() => setEditing(null)}
          onSaved={() => {
            setEditing(null);
            setReloadKey((k) => k + 1);
          }}
        />
      )}
    </div>
  );
}

function TemplateForm({
  row,
  onClose,
  onSaved,
}: {
  row: TemplateRow;
  onClose: () => void;
  onSaved: () => void;
}) {
  const [subject, setSubject] = useState(row.subject);
  const [body, setBody] = useState(row.body);
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const offenders = unknownTokens(row.code, subject, body);
  const empty = subject.trim() === '' || body.trim() === '';

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setProblem(null);

    const result = await saveTemplate(row, subject, body);
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  // A pop-up rather than a panel below the table. Which letter is being edited is a modal
  // decision, and leaving the table live invited a second Edit click that swapped the
  // form's subject out from under a half-typed one with nothing said. Modal brings Escape,
  // the backdrop and the focus move with it.
  return (
    <Modal title={row.name} onClose={onClose}>
      <form className="templates__form" onSubmit={submit}>
        {!row.stored && (
          <p className="templates__hint">
            This letter has no saved wording yet, so it is currently sent using the built-in
            copy. Saving here replaces it.
          </p>
        )}

        <label htmlFor="template-subject">Subject</label>
        <input
          id="template-subject"
          type="text"
          value={subject}
          onChange={(e) => setSubject(e.target.value)}
        />

        <label htmlFor="template-body">Body</label>
        <textarea
          id="template-body"
          rows={12}
          value={body}
          onChange={(e) => setBody(e.target.value)}
        />
        <p className="templates__hint">
          {row.isHtml
            ? 'This letter is HTML — your paragraph tags are kept as written.'
            : 'This letter is plain text.'}
        </p>

        <p className="templates__hint">
          Tokens this letter fills in:{' '}
          {row.tokens.map((t) => (
            <code key={t} className="templates__token">{`{{${t}}}`}</code>
          ))}
        </p>

        {offenders.length > 0 && (
          <p className="templates__problem">
            This letter does not supply{' '}
            {offenders.map((t) => `{{${t}}}`).join(', ')}
            {offenders.length === 1
              ? ' — it would render as a gap.'
              : ' — they would render as gaps.'}
          </p>
        )}

        {empty && (
          <p className="templates__problem">
            A subject and a body are both needed. Saving one empty would quietly fall back to
            the built-in wording instead of sending what you wrote.
          </p>
        )}

        {problem && <p className="templates__problem">{problem}</p>}

        <div className="templates__actions">
          {/*
            Disabled on what this page can see, not as the authority. The plug-in refuses the
            same things and its sentence is what appears above on a failed save.
          */}
          <button
            type="submit"
            className="templates__btn"
            disabled={saving || offenders.length > 0 || empty}
          >
            {saving ? 'Saving…' : 'Save'}
          </button>
          <button
            type="button"
            className="templates__btn templates__btn--ghost"
            onClick={onClose}
            disabled={saving}
          >
            Cancel
          </button>
        </div>
      </form>
    </Modal>
  );
}
