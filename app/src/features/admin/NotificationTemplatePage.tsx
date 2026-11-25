import { useEffect, useId, useRef, useState } from 'react';
import { Modal } from '../../components/feedback/Modal';
import {
  RichTextEditor,
  type RichTextEditorHandle,
} from '../../components/form/RichTextEditor';
import { PageIntro } from '../../components/layout/PageIntro';
import { usePermissions } from '../../app/permissions/permissionContext';
import { builtInWording, letterWordingMissing, unknownTokens } from './notificationTemplates';
import { dateTime } from '../../lib/format';
import {
  CUSTOM_TOKENS,
  ROLE_KINDS,
  GRADINGS,
  TRIGGER_EVENTS,
  eventLabel,
  tokensFor,
  normaliseCode,
  recipientLabel,
  unknownCustomTokens,
  type Recipients,
} from './notificationRouting';
import {
  FAILED_LETTER_DAYS,
  useFailedLetters,
  useNotificationTemplates,
  saveTemplate,
  createTemplate,
  removeTemplate,
  type ContactOption,
  type TemplateRow,
} from './useNotificationTemplates';
import { removalPrompt } from './notificationTemplateRemoval';
import { TokenPicker } from './TokenPicker';
import { FilterBar, FilterField } from '../../components/form/FilterBar';
import { Icon } from '../../components/icons/Icon';
import { groupLetters, stageNameOf } from './letterGroups';
import { previewBody, previewSubject, splitTokens, tokensInUse } from './templatePreview';
import './NotificationTemplatePage.css';
import { Skeleton } from '../../components/feedback/Skeleton';
import { Pager } from '../../components/navigation/Pager';
import { PAGE_SIZE, usePaged } from '../../components/navigation/paging';

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
  const failed = useFailedLetters(reloadKey);
  const { can } = usePermissions();
  const canEdit = can('page.admin.templates', 'Manage');

  const [editing, setEditing] = useState<TemplateRow | 'new' | null>(null);
  const [removing, setRemoving] = useState<TemplateRow | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const reload = () => {
    setEditing(null);
    setRemoving(null);
    setProblem(null);
    setReloadKey((k) => k + 1);
  };

  const [search, setSearch] = useState('');
  const [wording, setWording] = useState<'all' | 'edited' | 'builtin'>('all');

  const templates = state.status === 'ready' ? state.templates : [];
  const edited = templates.filter((row) => row.stored).length;
  const needle = search.trim().toLowerCase();
  const shown = templates.filter(
    (row) =>
      (wording === 'all' || (wording === 'edited') === row.stored) &&
      (needle === '' || row.name.toLowerCase().includes(needle) || row.code.toLowerCase().includes(needle)),
  );
  // Paged before grouping, so a page holds fifteen letters whichever stages they fall in.
  const paged = usePaged(shown, PAGE_SIZE, `${wording}|${needle}`);

  return (
    <div className="templates">
      <PageIntro
        title="Email templates"
        purpose="The letters the system sends as a case moves along. Edit the wording, or go back to the built-in version at any time."
        actions={
          canEdit && state.status === 'ready' ? (
            <button type="button" className="button-primary" onClick={() => setEditing('new')}>
              <Icon name="plus" />
              Add a letter
            </button>
          ) : undefined
        }
      />

      {state.status === 'loading' && <Skeleton label="Loading templates…" rows={6} />}
      {state.status === 'unavailable' && <p className="templates__note">{state.reason}</p>}

      {state.status === 'ready' && (
        <>
          <FilterBar summary={`${edited} edited, ${templates.length - edited} using built-in wording`}>
            <FilterField label="Search letters" htmlFor="templates-search" search>
              <input
                id="templates-search"
                type="search"
                placeholder="Letter name or code"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
              />
            </FilterField>
            <FilterField label="Wording" htmlFor="templates-wording">
              <select
                id="templates-wording"
                value={wording}
                onChange={(e) => setWording(e.target.value as typeof wording)}
              >
                <option value="all">All</option>
                <option value="edited">Edited</option>
                <option value="builtin">Built-in</option>
              </select>
            </FilterField>
          </FilterBar>

          <p className="templates__info">
            <Icon name="info" />
            <span>
              Every letter here is sent when the system raises it, to whoever the letter is about. Letters with a
              different trigger or recipient say so on their row.
            </span>
          </p>

          {shown.length === 0 && <p className="templates__note">No letter matches these filters.</p>}

          {groupLetters(paged.rows).map((group) => (
            <section key={group.key} className="templates__group" aria-labelledby={`letters-${group.key}`}>
              <div className="templates__group-head">
                <h2 id={`letters-${group.key}`}>{group.name}</h2>
                <p>{group.description}</p>
                <p className="templates__group-count">
                  {group.letters.length} {group.letters.length === 1 ? 'letter' : 'letters'}
                </p>
              </div>
              <ul className="templates__letters">
                {group.letters.map((row) => {
                  const names = recipientNames(row.recipients, state.contacts, state.contactsKnown);
                  return (
                    <li key={row.code} className="templates__letter">
                      <span className="templates__letter-icon">
                        <Icon name="mail" />
                      </span>
                      <div className="templates__letter-text">
                        <span className="templates__name">{row.name}</span>
                        <span className="templates__code">{row.code}</span>
                        {row.custom && (
                          <span className="templates__routing">
                            Sent at:{' '}
                            {eventLabel(row.eventValue) ?? (
                              <span className="templates__problem-cell">Nothing sends this</span>
                            )}
                          </span>
                        )}
                        {names.length > 0 && (
                          <span className="templates__routing">Goes to: {names.join(', ')}</span>
                        )}
                        {row.gradings.length > 0 && (
                          <span className="templates__routing">Only for: {gradingNames(row.gradings)}</span>
                        )}
                      </div>
                      <span className="templates__state">
                        {row.custom ? (
                          <span className="templates__chip">Your own letter</span>
                        ) : row.stored ? (
                          <span className="templates__chip">
                            <Icon name="pencil" />
                            Edited
                          </span>
                        ) : (
                          <span className="templates__chip templates__chip--builtin">Built-in wording</span>
                        )}
                      </span>
                      {canEdit && (
                        <span className="templates__row-actions">
                          {/*
                            Only where there is a row to take away. One of the twelve that nobody
                            has edited is already on its built-in wording, so the action would do
                            nothing and saying so is worse than not offering it.
                          */}
                          {removalPrompt(row) && (
                            <button
                              type="button"
                              className="button-quiet"
                              onClick={() => {
                                setProblem(null);
                                setRemoving(row);
                              }}
                            >
                              {row.custom ? <Icon name="trash" /> : <Icon name="restore" />}
                              {row.custom ? 'Remove' : 'Restore built-in'}
                              <span className="templates__sr"> wording for {row.name}</span>
                            </button>
                          )}
                          <button type="button" className="button-secondary" onClick={() => setEditing(row)}>
                            Edit<span className="templates__sr"> {row.name}</span>
                          </button>
                        </span>
                      )}
                    </li>
                  );
                })}
              </ul>
            </section>
          ))}

          {shown.length > 0 && <Pager {...paged} label="letters" />}

          {editing === 'new' && (
            <NewTemplateForm
              contacts={state.contacts}
              contactsKnown={state.contactsKnown}
              taken={state.templates.map((t) => t.code)}
              onClose={() => setEditing(null)}
              onSaved={reload}
            />
          )}

          {editing !== null && editing !== 'new' && (
            <TemplateForm
              row={editing}
              contacts={state.contacts}
              contactsKnown={state.contactsKnown}
              onClose={() => setEditing(null)}
              onSaved={reload}
              onRestore={() => {
                setEditing(null);
                setProblem(null);
                setRemoving(editing);
              }}
            />
          )}

          {problem && <p className="templates__problem">{problem}</p>}

          <NotSent state={failed} />

          {removing && (
            <RemoveTemplate
              row={removing}
              onClose={() => setRemoving(null)}
              onRemoved={reload}
              onProblem={(reason) => {
                setRemoving(null);
                setProblem(reason);
              }}
            />
          )}
        </>
      )}
    </div>
  );
}

const UNREACHABLE = 'A contact who cannot be reached';

/** The gradings a letter is limited to, in words. */
function gradingNames(gradings: number[]): string {
  return GRADINGS.filter((g) => gradings.includes(g.value))
    .map((g) => g.label)
    .join(', ');
}

/** Which AQS gradings a letter of your own goes out for. None ticked: whatever the grading. */
export function GradingFields({
  gradings,
  setGradings,
  disabled,
}: {
  gradings: number[];
  setGradings: (value: number[]) => void;
  disabled: boolean;
}) {
  return (
    <fieldset className="recipients">
      <legend className="recipients__heading">Only for these gradings</legend>
      <div className="recipients__kinds">
        {GRADINGS.map((g) => (
          <label key={g.value} className="recipients__kind">
            <input
              type="checkbox"
              checked={gradings.includes(g.value)}
              disabled={disabled}
              onChange={(e) =>
                setGradings(e.target.checked ? [...gradings, g.value] : gradings.filter((v) => v !== g.value))
              }
            />
            {g.label}
          </label>
        ))}
      </div>
      <p className="templates__hint">
        The AQS check&rsquo;s grading decides, even when Tax checked the case too. Leave all unticked to send the
        letter whatever the grading. A letter limited to gradings is not sent before the AQS check has graded the
        case.
      </p>
    </fieldset>
  );
}

/**
 * Who a row goes to, in words: the roles ticked, then the contacts by name. A contact the page
 * cannot name is no longer listed as reachable (inactive, or no work email), and says so.
 */
function recipientNames(recipients: Recipients, contacts: ContactOption[], contactsKnown: boolean): string[] {
  return [
    ...recipients.kinds.map((k) => recipientLabel(k) ?? String(k)),
    ...recipients.contactIds.map(
      (id) => contacts.find((c) => c.id === id)?.name ?? (contactsKnown ? UNREACHABLE : 'A named contact'),
    ),
  ];
}

/**
 * Letters that could not be sent, and why.
 *
 * Without this a letter could fail with nothing anywhere a person would look - the PROD fault
 * of 2026-10-15 was three letters that never went and a save that had said nothing was wrong.
 * "Sent" in the outbox means handed to the mail server, so this lists only what the system
 * itself knows went wrong.
 */
function NotSent({ state }: { state: ReturnType<typeof useFailedLetters> }) {
  const page = usePaged(state.status === 'ready' ? state.letters : []);
  return (
    <section className="templates__failed" aria-labelledby="templates-failed-heading">
      <h2 id="templates-failed-heading" className="templates__failed-heading">
        Not sent
      </h2>

      {state.status === 'loading' && <Skeleton label="Loading the letters that were not sent…" />}

      {state.status === 'unavailable' && (
        <p className="templates__problem">
          The letters that could not be sent cannot be listed right now, so this page cannot
          say whether any have failed.
        </p>
      )}

      {state.status === 'ready' && state.letters.length === 0 && (
        <p className="templates__note">
          No letter has failed in the last {FAILED_LETTER_DAYS} days.
        </p>
      )}

      {state.status === 'ready' && state.letters.length > 0 && (
        <>
          <p className="templates__hint">
            {state.letters.length === 1 ? 'One letter' : `${state.letters.length} letters`} in
            the last {FAILED_LETTER_DAYS} days could not be sent. Nothing is retried on its own:
            fix the cause below, and the next letter will go.
          </p>
          <table className="templates__table">
            <thead>
              <tr>
                <th scope="col">When</th>
                <th scope="col">Letter</th>
                <th scope="col">To</th>
                <th scope="col">Why it was not sent</th>
              </tr>
            </thead>
            <tbody>
              {page.rows.map((letter) => (
                <tr key={letter.id}>
                  <td>{dateTime(letter.queuedOn)}</td>
                  <td>{letter.subject}</td>
                  <td>{letter.recipient ?? <span className="templates__builtin">Nobody</span>}</td>
                  <td>{letter.reason}</td>
                </tr>
              ))}
            </tbody>
          </table>
          <Pager {...page} label="letters" />
        </>
      )}
    </section>
  );
}

/**
 * Confirms taking a stored letter off the table (F24).
 *
 * Confirmed rather than done on the first click, because the row is deleted and the wording
 * cannot be recovered from this page. The dialog says what stops AND what does not, which is
 * the part that matters: on a page listing twelve letters the system sends, "remove" invites
 * the fear that an event is being switched off. It is not — the built-in letter for that
 * event carries on exactly as before.
 *
 * All the words come from `removalPrompt`, because the two cases read very differently: a
 * letter of your own stops existing, and one of the twelve carries on being sent in its
 * original wording.
 */
function RemoveTemplate({
  row,
  onClose,
  onRemoved,
  onProblem,
}: {
  row: TemplateRow;
  onClose: () => void;
  onRemoved: () => void;
  onProblem: (reason: string) => void;
}) {
  const [removing, setRemoving] = useState(false);
  const prompt = removalPrompt(row);

  if (prompt === null || row.id === null) return null;

  async function confirm() {
    setRemoving(true);
    const result = await removeTemplate(row.id!);
    setRemoving(false);

    if (result.ok) {
      onRemoved();
      return;
    }

    onProblem(result.reason);
  }

  return (
    <Modal title={prompt.title} onClose={onClose}>
      <div className="templates__form">
        <p>{prompt.lead}</p>
        <p className="templates__hint">{prompt.consequence}</p>

        <div className="templates__actions">
          <button type="button" className="templates__btn" onClick={confirm} disabled={removing}>
            {removing ? 'Working…' : prompt.confirmLabel}
          </button>
          <button
            type="button"
            className="templates__btn templates__btn--ghost"
            onClick={onClose}
            disabled={removing}
          >
            Cancel
          </button>
        </div>
      </div>
    </Modal>
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
 * The subject line with its tokens drawn as chips. The chips are a copy of the text laid
 * behind a transparent input, so the field stays a plain input (caret, selection, undo and
 * spliceAtCaret all behave as before) and only the painting changes. The chips use a shadow
 * rather than padding so the copy's text sits exactly under the input's.
 */
function SubjectField({
  value,
  onChange,
  onFocus,
  inputRef,
  disabled,
}: {
  value: string;
  onChange: (value: string) => void;
  onFocus: () => void;
  inputRef: React.RefObject<HTMLInputElement | null>;
  disabled: boolean;
}) {
  const mirrorRef = useRef<HTMLDivElement>(null);
  const sync = () => {
    if (mirrorRef.current && inputRef.current) mirrorRef.current.scrollLeft = inputRef.current.scrollLeft;
  };
  useEffect(sync);

  return (
    <div className="subject-field">
      <div ref={mirrorRef} className="subject-field__mirror" aria-hidden="true">
        {splitTokens(value).map((part, i) =>
          part.token ? (
            <mark key={i} className="subject-field__token">
              {part.text}
            </mark>
          ) : (
            <span key={i}>{part.text}</span>
          ),
        )}
      </div>
      <input
        id="template-subject"
        ref={inputRef}
        type="text"
        className="subject-field__input"
        value={value}
        disabled={disabled}
        spellCheck={false}
        onFocus={onFocus}
        onChange={(e) => onChange(e.target.value)}
        onScroll={sync}
        onKeyUp={sync}
        onSelect={sync}
      />
    </div>
  );
}

/** The letter filled with sample values, as a reader would get it. */
function LetterPreview({ subject, body }: { subject: string; body: string }) {
  const headingId = useId();
  return (
    <section className="preview" aria-labelledby={headingId}>
      <div className="preview__head">
        <h3 id={headingId} className="preview__heading">
          <Icon name="eye" />
          Preview
        </h3>
        <span className="preview__note">Filled with sample values</span>
      </div>
      <div className="preview__letter">
        <p className="preview__subject">{previewSubject(subject) || 'No subject yet'}</p>
        {/* Rebuilt from the server's allow-list with every attribute dropped: see previewBody. */}
        <div className="preview__body" dangerouslySetInnerHTML={{ __html: previewBody(body) }} />
      </div>
    </section>
  );
}

/** The body editor, its toolbar's jump to the field list, and the note on what is kept. */
function BodyField({
  editorRef,
  value,
  onChange,
  onFocus,
  disabled,
}: {
  editorRef: React.RefObject<RichTextEditorHandle | null>;
  value: string;
  onChange: (html: string) => void;
  onFocus: () => void;
  disabled: boolean;
}) {
  return (
    <div className="tedit__field">
      <label htmlFor="template-body">Body</label>
      {/*
        Every letter, not only the ones the catalogue calls HTML (AD-170). The body reaches
        the reader through email.description, which renders as markup whatever the letter is
        called - so there was never a plain-text letter in the sense the flag implied.
      */}
      {/* Focus bubbles, so one handler covers the editor's editable area. */}
      <div onFocus={onFocus}>
        <RichTextEditor
          id="template-body"
          ref={editorRef}
          value={value}
          onChange={onChange}
          label="Body"
          disabled={disabled}
          toolbarEnd={
            <button
              type="button"
              className="rte__btn"
              title="Insert a field"
              aria-label="Insert a field"
              disabled={disabled}
              // The caret stays in the body, so the field chosen next lands where it was.
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => document.querySelector<HTMLButtonElement>(`#${FIELDS_ID} .tokens__add`)?.focus()}
            >
              <Icon name="braces" />
            </button>
          }
        />
      </div>
      <p className="templates__hint">
        Formatting is kept as you set it here. Anything beyond the toolbar’s formatting and links is removed when
        you save.
      </p>
    </div>
  );
}

const FIELDS_ID = 'template-fields';

/**
 * Who a letter goes to, shared by both forms: any of the roles, and any number of named
 * contacts. Each person gets their own copy.
 *
 * Choosing nobody on a built-in letter is a real choice and the default one: the code that
 * raises it already works out who it is about, and that is right far more often than a
 * blanket rule would be.
 */
function RecipientFields({
  recipients,
  setRecipients,
  contacts,
  contactsKnown,
  allowDefault,
}: {
  recipients: Recipients;
  setRecipients: (value: Recipients) => void;
  contacts: ContactOption[];
  contactsKnown: boolean;
  allowDefault: boolean;
}) {
  const { kinds, contactIds } = recipients;
  const available = contacts.filter((c) => !contactIds.includes(c.id));
  const noContacts = contactsKnown ? 'No other contacts to add' : 'The contact list could not be loaded';

  function toggleKind(value: number, on: boolean) {
    setRecipients({
      kinds: on ? [...kinds, value] : kinds.filter((k) => k !== value),
      contactIds,
    });
  }

  return (
    <fieldset className="recipients">
      <legend className="recipients__heading">Goes to</legend>

      {allowDefault && kinds.length === 0 && contactIds.length === 0 && (
        <p className="recipients__default">
          <Icon name="info" />
          <span>
            Nobody chosen, so this letter goes to whoever it is about: the adviser on the case, the checker who
            submitted it, and so on. Choose people only to replace that.
          </span>
        </p>
      )}

      <div className="recipients__kinds">
        {ROLE_KINDS.map((r) => (
          <label key={r.value} className="recipients__kind">
            <input
              type="checkbox"
              checked={kinds.includes(r.value)}
              onChange={(e) => toggleKind(r.value, e.target.checked)}
            />
            {r.label}
          </label>
        ))}
      </div>

      {contactIds.length > 0 && (
        <ul className="recipients__chosen" aria-label="Named contacts">
          {contactIds.map((id) => {
            const contact = contacts.find((c) => c.id === id);
            return (
              <li key={id}>
                {contact ? (
                  `${contact.name} (${contact.email})`
                ) : (
                  <span className="templates__problem-cell">{contactsKnown ? UNREACHABLE : 'A named contact'}</span>
                )}
                <button
                  type="button"
                  className="button-quiet"
                  onClick={() => setRecipients({ kinds, contactIds: contactIds.filter((c) => c !== id) })}
                >
                  Remove
                  <span className="templates__sr"> {contact?.name ?? 'this contact'}</span>
                </button>
              </li>
            );
          })}
        </ul>
      )}

      <label htmlFor="template-contact" className="recipients__label">
        Add a named contact
      </label>
      <select
        id="template-contact"
        value=""
        onChange={(e) => {
          if (e.target.value !== '') {
            setRecipients({ kinds, contactIds: [...contactIds, e.target.value] });
          }
        }}
      >
        <option value="">{available.length > 0 ? 'Choose a contact' : noContacts}</option>
        {available.map((c) => (
          <option key={c.id} value={c.id}>
            {c.name} ({c.email})
          </option>
        ))}
      </select>
      <p className="templates__hint">The same person for every case. Only contacts with a work email are listed.</p>
    </fieldset>
  );
}

/** True when a chosen contact can no longer be written to; the plug-in refuses the save. */
function hasUnreachableContact(recipients: Recipients, contacts: ContactOption[], contactsKnown: boolean): boolean {
  return contactsKnown && recipients.contactIds.some((id) => !contacts.some((c) => c.id === id));
}

/** Subject, body and token handling shared by both forms. */
function useLetterWording(initialSubject: string, initialBody: string) {
  const [subject, setSubject] = useState(initialSubject);
  const [body, setBody] = useState(initialBody);
  // Which field a token goes into. Tracked rather than assumed, and named on the picker:
  // dropping one into the body when the person had just clicked into the subject is the kind
  // of mistake that is only noticed after the letter has gone.
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

  return { subject, setSubject, body, setBody, focused, setFocused, subjectRef, editorRef, insertToken };
}

function TemplateForm({
  row,
  contacts,
  contactsKnown,
  onClose,
  onSaved,
  onRestore,
}: {
  row: TemplateRow;
  contacts: ContactOption[];
  contactsKnown: boolean;
  onClose: () => void;
  onSaved: () => void;
  /** Hands the letter to the page's restore confirmation. */
  onRestore: () => void;
}) {
  /*
   * A letter with no stored row opens on the wording it is actually being sent with (F28).
   *
   * It used to open empty, so the page whose stated purpose is "the subject and body of
   * every email this system sends" showed neither, for exactly the letters running on the
   * built-in copy. Anyone wanting to change one sentence of the standard wording had to
   * retype the whole letter, because they could not see it.
   *
   * Nothing is written until Save, so opening and closing still leaves the letter on its
   * built-in copy, and the hint above the subject says so.
   */
  const builtIn = row.custom ? null : builtInWording(row.code);
  const wording = useLetterWording(
    row.stored ? row.subject : (builtIn?.subject ?? ''),
    row.stored ? row.body : (builtIn?.body ?? ''),
  );
  const { subject, body } = wording;
  const [recipients, setRecipients] = useState<Recipients>(row.recipients);
  const [gradings, setGradings] = useState<number[]>(row.gradings);
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const formId = useId();

  const offenders = row.custom
    ? unknownCustomTokens(subject, body)
    : unknownTokens(row.code, subject, body);
  const empty = letterWordingMissing(subject, body);
  const unreachable = hasUnreachableContact(recipients, contacts, contactsKnown);
  const nobody = row.custom && recipients.kinds.length === 0 && recipients.contactIds.length === 0;

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setSaving(true);
    setProblem(null);

    const result = await saveTemplate(row, subject, body, recipients, gradings);
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
    <Modal
      size="editor"
      title={row.name}
      onClose={onClose}
      meta={
        <span className="tedit__meta">
          <span className="tedit__stage">{stageNameOf(row)}</span>
          {row.custom ? (
            <span className="templates__chip">Your own letter</span>
          ) : row.stored ? (
            <span className="templates__chip">
              <Icon name="pencil" />
              Edited
            </span>
          ) : (
            <span className="templates__chip templates__chip--builtin">Built-in wording</span>
          )}
        </span>
      }
      footer={
        <>
          <span>
            {/* Only where there is saved wording to take away; the restore itself is confirmed. */}
            {!row.custom && row.stored ? (
              <button type="button" className="button-quiet" onClick={onRestore} disabled={saving}>
                <Icon name="restore" />
                Restore built-in wording
              </button>
            ) : null}
          </span>
          <span className="tedit__actions">
            <button type="button" className="button-secondary" onClick={onClose} disabled={saving}>
              Cancel
            </button>
            {/*
              Disabled on what this page can see, not as the authority. The plug-in refuses the
              same things and its sentence is what appears above on a failed save.
            */}
            <button
              type="submit"
              form={formId}
              className="button-primary"
              disabled={saving || offenders.length > 0 || empty || unreachable || nobody}
            >
              {saving ? 'Saving…' : 'Save letter'}
            </button>
          </span>
        </>
      }
    >
      <form id={formId} className="tedit" onSubmit={submit}>
        <div className="tedit__main">
          {!row.stored && (
            <p className="templates__hint">
              This letter has no saved wording yet, so it is currently sent using the built-in copy, which is what
              is shown below. Saving here replaces it; closing without saving leaves the letter on the built-in
              copy.
            </p>
          )}

          {row.custom && (
            <p className="templates__hint">
              Sent at: <strong>{eventLabel(row.eventValue) ?? 'nothing sends this yet'}</strong>.
            </p>
          )}

          <div className="tedit__field">
            <label htmlFor="template-subject">Subject</label>
            <SubjectField
              value={subject}
              onChange={wording.setSubject}
              onFocus={() => wording.setFocused('subject')}
              inputRef={wording.subjectRef}
              disabled={saving}
            />
          </div>

          <BodyField
            editorRef={wording.editorRef}
            value={body}
            onChange={wording.setBody}
            onFocus={() => wording.setFocused('body')}
            disabled={saving}
          />

          {offenders.length > 0 && (
            <p className="templates__problem">
              This letter does not supply {offenders.map((t) => `{{${t}}}`).join(', ')}
              {offenders.length === 1 ? ', so it would render as a gap.' : ', so they would render as gaps.'}
            </p>
          )}

          {empty && (
            <p className="templates__problem">
              A subject and a body are both needed. Saving one empty would quietly fall back to the built-in
              wording instead of sending what you wrote.
            </p>
          )}

          {unreachable && (
            <p className="templates__problem">
              A chosen contact is inactive or has no work email, so this letter could never reach them. Remove
              them, or fix their contact record.
            </p>
          )}

          {nobody && <p className="templates__problem">Choose who should receive this letter.</p>}

          {problem && <p className="templates__problem">{problem}</p>}

          <LetterPreview subject={subject} body={body} />
        </div>

        <aside className="tedit__side" aria-label="Recipients and fields">
          <RecipientFields
            recipients={recipients}
            setRecipients={setRecipients}
            contacts={contacts}
            contactsKnown={contactsKnown}
            allowDefault={!row.custom}
          />

          {row.custom && <GradingFields gradings={gradings} setGradings={setGradings} disabled={saving} />}

          <TokenPicker
            id={FIELDS_ID}
            tokens={tokensFor(row.tokens)}
            used={tokensInUse(subject, body)}
            onInsert={wording.insertToken}
            targetLabel={wording.focused === 'subject' ? 'Subject' : 'Body'}
            disabled={saving}
          />
        </aside>
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
  contactsKnown,
  taken,
  onClose,
  onSaved,
}: {
  contacts: ContactOption[];
  contactsKnown: boolean;
  taken: string[];
  onClose: () => void;
  onSaved: () => void;
}) {
  const [name, setName] = useState('');
  const [code, setCode] = useState('');
  const [eventValue, setEventValue] = useState<number | null>(null);
  const [recipients, setRecipients] = useState<Recipients>({ kinds: [], contactIds: [] });
  const [gradings, setGradings] = useState<number[]>([]);
  const wording = useLetterWording('', '');
  const { subject, body } = wording;
  const [problem, setProblem] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const formId = useId();

  // The code is the table's alternate key, so it is normalised as it is typed rather than
  // saved as written: "tell the manager" and "TELL-THE-MANAGER" must not become two rows.
  const normalised = normaliseCode(code === '' ? name : code);
  const clashes = taken.includes(normalised);

  const offenders = unknownCustomTokens(subject, body);
  const unreachable = hasUnreachableContact(recipients, contacts, contactsKnown);
  const incomplete =
    name.trim() === '' ||
    normalised === '' ||
    eventValue === null ||
    (recipients.kinds.length === 0 && recipients.contactIds.length === 0) ||
    letterWordingMissing(subject, body);

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
      recipients,
      gradings,
    });
    setSaving(false);

    if (result.ok) {
      onSaved();
      return;
    }

    setProblem(result.reason);
  }

  return (
    <Modal
      size="editor"
      title="Add a letter"
      onClose={onClose}
      meta={<span className="templates__chip">Your own letter</span>}
      footer={
        <>
          <span />
          <span className="tedit__actions">
            <button type="button" className="button-secondary" onClick={onClose} disabled={saving}>
              Cancel
            </button>
            <button
              type="submit"
              form={formId}
              className="button-primary"
              disabled={saving || incomplete || clashes || offenders.length > 0 || unreachable}
            >
              {saving ? 'Saving…' : 'Add the letter'}
            </button>
          </span>
        </>
      }
    >
      <form id={formId} className="tedit" onSubmit={submit}>
        <div className="tedit__main">
          <p className="templates__hint">
            This letter is sent in addition to whatever the system already sends at that moment. It does not
            replace it.
          </p>

          <div className="tedit__row">
            <div className="tedit__field">
              <label htmlFor="template-name">Name</label>
              <input id="template-name" type="text" value={name} onChange={(e) => setName(e.target.value)} />
            </div>

            <div className="tedit__field">
              <label htmlFor="template-code">Code</label>
              <input
                id="template-code"
                type="text"
                value={code}
                placeholder={normaliseCode(name)}
                onChange={(e) => setCode(e.target.value)}
              />
              <p className="templates__hint">
                Saved as <code className="templates__token">{normalised || '—'}</code>. Taken from the name unless
                you set one.
              </p>
            </div>

            <div className="tedit__field">
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
            </div>
          </div>

          <div className="tedit__field">
            <label htmlFor="template-subject">Subject</label>
            <SubjectField
              value={subject}
              onChange={wording.setSubject}
              onFocus={() => wording.setFocused('subject')}
              inputRef={wording.subjectRef}
              disabled={saving}
            />
          </div>

          <BodyField
            editorRef={wording.editorRef}
            value={body}
            onChange={wording.setBody}
            onFocus={() => wording.setFocused('body')}
            disabled={saving}
          />

          {clashes && (
            <p className="templates__problem">
              A letter with the code {normalised} already exists. Give this one a different name or code.
            </p>
          )}

          {offenders.length > 0 && (
            <p className="templates__problem">
              A letter of your own cannot use {offenders.map((t) => `{{${t}}}`).join(', ')}
              {offenders.length === 1 ? ', so it would render as a gap.' : ', so they would render as gaps.'}
            </p>
          )}

          {unreachable && (
            <p className="templates__problem">
              A chosen contact has no work email, so this letter could never reach them.
            </p>
          )}

          {problem && <p className="templates__problem">{problem}</p>}

          <LetterPreview subject={subject} body={body} />
        </div>

        <aside className="tedit__side" aria-label="Recipients and fields">
          <RecipientFields
            recipients={recipients}
            setRecipients={setRecipients}
            contacts={contacts}
            contactsKnown={contactsKnown}
            allowDefault={false}
          />

          <GradingFields gradings={gradings} setGradings={setGradings} disabled={saving} />

          <TokenPicker
            id={FIELDS_ID}
            tokens={tokensFor(CUSTOM_TOKENS)}
            used={tokensInUse(subject, body)}
            onInsert={wording.insertToken}
            targetLabel={wording.focused === 'subject' ? 'Subject' : 'Body'}
            disabled={saving}
          />
        </aside>
      </form>
    </Modal>
  );
}
