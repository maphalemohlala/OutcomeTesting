import { useRef, useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import {
  RichTextEditor,
  type RichTextEditorHandle,
} from '../../components/form/RichTextEditor';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { unknownTokens } from './notificationTemplates';
import {
  CUSTOM_TOKENS,
  KIND_CONTACT,
  RECIPIENT_KINDS,
  TRIGGER_EVENTS,
  eventLabel,
  tokensFor,
  normaliseCode,
  recipientLabel,
  unknownCustomTokens,
} from './notificationRouting';
import {
  useNotificationTemplates,
  saveTemplate,
  createTemplate,
  type ContactOption,
  type TemplateRow,
} from './useNotificationTemplates';
import { TokenPicker } from './TokenPicker';
import './NotificationTemplatePage.css';

/**
 * The wording of every letter the solution sends, who it goes to, and what sends it
 * (Change 1, AD-163; recipients and letters of your own, AD-168).
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

  const [editing, setEditing] = useState<TemplateRow | 'new' | null>(null);

  const reload = () => {
    setEditing(null);
    setReloadKey((k) => k + 1);
  };

  return (
    <div className="templates">
      <PageIntro
        title="Notification wording"
        purpose={
          'The subject and body of every email this system sends, and who receives it. A ' +
          'letter you have not edited uses the built-in wording, and changing one takes ' +
          'effect on the next send.'
        }
      />

      {state.status === 'loading' && <p className="templates__note">Loading…</p>}
      {state.status === 'unavailable' && <p className="templates__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <>
          {canEdit && (
            <button
              type="button"
              className="templates__btn templates__add"
              onClick={() => setEditing('new')}
            >
              Add a letter
            </button>
          )}

          <table className="templates__table">
            <thead>
              <tr>
                <th scope="col">Letter</th>
                <th scope="col">Sent at</th>
                <th scope="col">Goes to</th>
                <th scope="col">Wording</th>
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
                    {row.custom ? (
                      (eventLabel(row.eventValue) ?? (
                        <span className="templates__problem-cell">Nothing sends this</span>
                      ))
                    ) : (
                      <span className="templates__builtin">When the system raises it</span>
                    )}
                  </td>
                  <td>
                    {recipientLabel(row.recipientKind) ? (
                      <>
                        {recipientLabel(row.recipientKind)}
                        {row.recipientKind === KIND_CONTACT && row.recipientContactName && (
                          <span className="templates__code">{row.recipientContactName}</span>
                        )}
                      </>
                    ) : (
                      <span className="templates__builtin">Whoever the letter is about</span>
                    )}
                  </td>
                  <td>
                    {row.stored ? 'Edited' : <span className="templates__builtin">Built-in</span>}
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

          {editing === 'new' && (
            <NewTemplateForm
              contacts={state.contacts}
              taken={state.templates.map((t) => t.code)}
              onClose={() => setEditing(null)}
              onSaved={reload}
            />
          )}

          {editing !== null && editing !== 'new' && (
            <TemplateForm
              row={editing}
              contacts={state.contacts}
              onClose={() => setEditing(null)}
              onSaved={reload}
            />
          )}
        </>
      )}
    </div>
  );
}

/**
 * Puts text in at the caret of a plain field and returns the new value.
 *
 * The caret is restored after React has re-rendered from the state this returns; setting the
 * value alone sends it to the end, so inserting a token mid-sentence would move the cursor to
 * the bottom of the letter each time.
 */
function spliceAtCaret(el: HTMLInputElement | HTMLTextAreaElement, text: string): string {
  const start = el.selectionStart ?? el.value.length;
  const end = el.selectionEnd ?? start;
  const next = el.value.slice(0, start) + text + el.value.slice(end);
  const caret = start + text.length;

  requestAnimationFrame(() => {
    el.focus();
    el.setSelectionRange(caret, caret);
  });

  return next;
}

/**
 * Who a letter goes to, shared by both forms.
 *
 * Leaving it unset on a built-in letter is a real choice and the default one: the code that
 * raises it already works out who it is about, and that is right far more often than a
 * blanket rule would be.
 */
function RecipientFields({
  kind,
  setKind,
  contactId,
  setContactId,
  contacts,
  allowDefault,
}: {
  kind: number | null;
  setKind: (value: number | null) => void;
  contactId: string | null;
  setContactId: (value: string | null) => void;
  contacts: ContactOption[];
  allowDefault: boolean;
}) {
  return (
    <>
      <label htmlFor="template-recipient">Goes to</label>
      <select
        id="template-recipient"
        value={kind === null ? '' : String(kind)}
        onChange={(e) => setKind(e.target.value === '' ? null : Number(e.target.value))}
      >
        {allowDefault && <option value="">Whoever the letter is about (recommended)</option>}
        {!allowDefault && <option value="">Choose…</option>}
        {RECIPIENT_KINDS.map((r) => (
          <option key={r.value} value={r.value}>
            {r.label}
          </option>
        ))}
      </select>
      {allowDefault && kind === null && (
        <p className="templates__hint">
          The code that raises this letter works out who it is about — the adviser on the case,
          the checker who submitted it, and so on. Choose somebody here only to override that.
        </p>
      )}

      {kind === KIND_CONTACT && (
        <>
          <label htmlFor="template-contact">Which contact</label>
          <select
            id="template-contact"
            value={contactId ?? ''}
            onChange={(e) => setContactId(e.target.value === '' ? null : e.target.value)}
          >
            <option value="">Choose…</option>
            {contacts.map((c) => (
              <option key={c.id} value={c.id}>
                {c.name} ({c.email})
              </option>
            ))}
          </select>
          <p className="templates__hint">
            The same person for every case. Only contacts with a work email are listed —
            somebody without one would be chosen here but never written to.
          </p>
        </>
      )}
    </>
  );
}

function TemplateForm({
  row,
  contacts,
  onClose,
  onSaved,
}: {
  row: TemplateRow;
  contacts: ContactOption[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [subject, setSubject] = useState(row.subject);
  const [body, setBody] = useState(row.body);
  const [kind, setKind] = useState<number | null>(row.recipientKind);
  // Which field a token goes into. Tracked rather than assumed, and named on the picker:
  // dropping one into the body when the person had just clicked into the subject is the kind
  // of mistake that is only noticed after the letter has gone.
  const [focused, setFocused] = useState<'subject' | 'body'>('body');
  const subjectRef = useRef<HTMLInputElement>(null);
  const editorRef = useRef<RichTextEditorHandle>(null);
  const [contactId, setContactId] = useState<string | null>(row.recipientContactId);
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  const offenders = row.custom
    ? unknownCustomTokens(subject, body)
    : unknownTokens(row.code, subject, body);
  const empty = subject.trim() === '' || body.trim() === '';
  const contactMissing = kind === KIND_CONTACT && !contactId;

  function insertToken(token: string) {
    const text = `{{${token}}}`;

    if (focused === 'subject') {
      const el = subjectRef.current;
      if (el) setSubject(spliceAtCaret(el, text));
      return;
    }

    editorRef.current?.insertText(text);
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setProblem(null);

    const result = await saveTemplate(row, subject, body, kind, contactId);
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

        {row.custom && (
          <p className="templates__hint">
            Sent at: <strong>{eventLabel(row.eventValue) ?? 'nothing sends this yet'}</strong>.
          </p>
        )}

        <label htmlFor="template-subject">Subject</label>
        <input
          id="template-subject"
          ref={subjectRef}
          type="text"
          value={subject}
          onFocus={() => setFocused('subject')}
          onChange={(e) => setSubject(e.target.value)}
        />

        <label htmlFor="template-body">Body</label>
        {/*
          A rich-text editor for an HTML letter and a plain box for a plain-text one. Putting
          the editor on both would quietly inject tags into a letter that is sent as plain
          text, and the reader would see the markup rather than the formatting.
        */}
        {/*
          Every letter, not only the ones the catalogue calls HTML (AD-170). The body reaches
          the reader through email.description, which renders as markup whatever the letter is
          called - so there was never a plain-text letter in the sense the flag implied, and
          nine of the twelve were getting a bare textarea for no reason a reader could see.
        */}
        {/* Focus bubbles, so one handler covers the editor's editable area. */}
        <div onFocus={() => setFocused('body')}>
          <RichTextEditor
            id="template-body"
            ref={editorRef}
            value={body}
            onChange={setBody}
            label="Body"
            disabled={saving}
          />
        </div>
        <p className="templates__hint">
          Formatting is kept as you set it here. Anything beyond the toolbar’s formatting and
          links is removed when you save.
        </p>

        <RecipientFields
          kind={kind}
          setKind={setKind}
          contactId={contactId}
          setContactId={setContactId}
          contacts={contacts}
          allowDefault={!row.custom}
        />

        <TokenPicker
          tokens={tokensFor(row.tokens)}
          onInsert={insertToken}
          targetLabel={focused === 'subject' ? 'Subject' : 'Body'}
          disabled={saving}
        />

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

        {contactMissing && (
          <p className="templates__problem">
            This letter is set to go to a named contact, but no contact is chosen.
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
            disabled={saving || offenders.length > 0 || empty || contactMissing}
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

/**
 * A letter of an administrator's own (AD-168).
 *
 * Unlike the twelve, this one exists only because its row says so: without an event nothing
 * would ever raise it, and without a recipient there is nobody to send it to. Both are
 * required here and refused server-side, because a row that looks like configuration and does
 * nothing is the failure mode worth being loud about.
 */
function NewTemplateForm({
  contacts,
  taken,
  onClose,
  onSaved,
}: {
  contacts: ContactOption[];
  taken: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [eventValue, setEventValue] = useState<number | null>(null);
  const [kind, setKind] = useState<number | null>(null);
  const [contactId, setContactId] = useState<string | null>(null);
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [focused, setFocused] = useState<'subject' | 'body'>('body');
  const subjectRef = useRef<HTMLInputElement>(null);
  const editorRef = useRef<RichTextEditorHandle>(null);

  function insertToken(token: string) {
    const text = `{{${token}}}`;

    if (focused === 'subject') {
      const el = subjectRef.current;
      if (el) setSubject(spliceAtCaret(el, text));
      return;
    }

    editorRef.current?.insertText(text);
  }

  // The code is the table's alternate key, so it is normalised as it is typed rather than
  // saved as written: "tell the manager" and "TELL-THE-MANAGER" must not become two rows.
  const normalised = normaliseCode(code === '' ? name : code);
  const clashes = taken.includes(normalised);

  const offenders = unknownCustomTokens(subject, body);
  const contactMissing = kind === KIND_CONTACT && !contactId;
  const incomplete =
    name.trim() === '' ||
    normalised === '' ||
    eventValue === null ||
    kind === null ||
    subject.trim() === '' ||
    body.trim() === '';

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setProblem(null);

    const result = await createTemplate({
      code: normalised,
      name: name.trim(),
      subject,
      body,
      eventValue,
      recipientKind: kind,
      recipientContactId: contactId,
    });
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  return (
    <Modal title="Add a letter" onClose={onClose}>
      <form className="templates__form" onSubmit={submit}>
        <p className="templates__hint">
          This letter is sent in addition to whatever the system already sends at that moment —
          it does not replace it.
        </p>

        <label htmlFor="template-name">Name</label>
        <input
          id="template-name"
          type="text"
          value={name}
          onChange={(e) => setName(e.target.value)}
        />

        <label htmlFor="template-code">Code</label>
        <input
          id="template-code"
          type="text"
          value={code}
          placeholder={normaliseCode(name)}
          onChange={(e) => setCode(e.target.value)}
        />
        <p className="templates__hint">
          Saved as <code className="templates__token">{normalised || '—'}</code>. Taken from the
          name unless you set one.
        </p>

        <label htmlFor="template-event">Sent at</label>
        <select
          id="template-event"
          value={eventValue === null ? '' : String(eventValue)}
          onChange={(e) => setEventValue(e.target.value === '' ? null : Number(e.target.value))}
        >
          <option value="">Choose…</option>
          {TRIGGER_EVENTS.map((t) => (
            <option key={t.value} value={t.value}>
              {t.label}
            </option>
          ))}
        </select>

        <RecipientFields
          kind={kind}
          setKind={setKind}
          contactId={contactId}
          setContactId={setContactId}
          contacts={contacts}
          allowDefault={false}
        />

        <label htmlFor="template-subject">Subject</label>
        <input
          id="template-subject"
          ref={subjectRef}
          type="text"
          value={subject}
          onFocus={() => setFocused('subject')}
          onChange={(e) => setSubject(e.target.value)}
        />

        <label htmlFor="template-body">Body</label>
        <div onFocus={() => setFocused('body')}>
          <RichTextEditor
            id="template-body"
            ref={editorRef}
            value={body}
            onChange={setBody}
            label="Body"
            disabled={saving}
          />
        </div>
        <p className="templates__hint">
          Formatting is kept as you set it here. Anything beyond the toolbar’s formatting and
          links is removed when you save.
        </p>

        <TokenPicker
          tokens={tokensFor(CUSTOM_TOKENS)}
          onInsert={insertToken}
          targetLabel={focused === 'subject' ? 'Subject' : 'Body'}
          disabled={saving}
        />

        {clashes && (
          <p className="templates__problem">
            A letter with the code {normalised} already exists. Give this one a different name
            or code.
          </p>
        )}

        {offenders.length > 0 && (
          <p className="templates__problem">
            A letter of your own cannot use{' '}
            {offenders.map((t) => `{{${t}}}`).join(', ')}
            {offenders.length === 1
              ? ' — it would render as a gap.'
              : ' — they would render as gaps.'}
          </p>
        )}

        {contactMissing && (
          <p className="templates__problem">
            This letter is set to go to a named contact, but no contact is chosen.
          </p>
        )}

        {problem && <p className="templates__problem">{problem}</p>}

        <div className="templates__actions">
          <button
            type="submit"
            className="templates__btn"
            disabled={saving || incomplete || clashes || offenders.length > 0 || contactMissing}
          >
            {saving ? 'Saving…' : 'Add the letter'}
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
