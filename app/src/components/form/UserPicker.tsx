import { useEffect, useId, useMemo, useState } from 'react';
import { useUserDirectory, type DirectoryUser } from '../../hooks/useUserDirectory';
import './UserPicker.css';

interface Props {
  id?: string;
  /** Stored value. What it means depends on `field`. */
  value: string;
  onChange: (value: string) => void;
  /** Shown as the empty-state hint. */
  placeholder?: string;
  /**
   * Which registry column the stored value is. A person field on a case holds the display
   * name (AD-029); a role assignment is keyed on work email (AD-010); an accountability
   * lookup holds the contact's id. All three are a choice of one person from the same
   * registry, so all three use this control and differ only in what they store.
   */
  field?: 'name' | 'email' | 'id';
}

/**
 * Person selector sourced from the application user registry (contact).
 *
 * The list itself is searchable (project owner, 2026-09-19): one control, typed into
 * directly, narrowing as you type. It was briefly a filter box ABOVE a select - two
 * controls for one field - which is what that instruction corrected.
 *
 * Built on a native datalist rather than a hand-rolled listbox. The browser owns the
 * filtering, the keyboard, the scrolling and the screen-reader semantics, none of which a
 * reimplementation gets right for free, and this codebase carries no UI dependency to do it
 * with.
 *
 * What a datalist costs is that it yields TEXT, not a record: it cannot carry a contact's
 * id. So each option's text is unique - name and email together where an id is wanted - and
 * `resolve` maps it back. Text matching nobody is kept for a name field, where imported
 * names that were never registered must survive (AD-029), and refused for an id field,
 * where a guess is not a person.
 */
export function UserPicker({
  id,
  value,
  onChange,
  placeholder = 'Search for a person',
  field = 'name',
}: Props) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const listId = `${inputId}-list`;
  const directory = useUserDirectory();

  const active = useMemo(
    () => (directory.status === 'ready' ? directory.users.filter((u) => u.active) : []),
    [directory],
  );

  /** What the person sees for one user, and what they type to choose them. */
  const optionText = (user: DirectoryUser) =>
    field === 'name'
      ? user.name
      : field === 'email'
        ? user.email
        : `${user.name} — ${user.email}`;

  const stored = (user: DirectoryUser) =>
    field === 'email' ? user.email : field === 'id' ? user.id : user.name;

  /** The text that stands for the value currently held. */
  const textFor = (held: string): string => {
    if (held === '') return '';
    const match = active.find((user) => stored(user) === held);
    // An id with no match shows empty rather than a raw guid: the person is gone from the
    // directory, and a guid in a text box reads as corruption.
    return match ? optionText(match) : field === 'id' ? '' : held;
  };

  const [text, setText] = useState(() => textFor(value));

  // The held value can change under the control - a save completes, a row reloads - and the
  // text has to follow it. Keyed on the value and the directory, so a value that arrives
  // before the directory does gets its name as soon as the names are known.
  useEffect(() => {
    setText(textFor(value));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, active]);

  if (directory.status !== 'ready') {
    return (
      <input
        id={inputId}
        type={field === 'email' ? 'email' : 'text'}
        className="user-picker__fallback"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={directory.status === 'loading' ? 'Loading people…' : placeholder}
      />
    );
  }

  const resolve = (typed: string): string | null => {
    const needle = typed.trim().toLowerCase();
    if (needle === '') return '';
    const match = active.find((user) => optionText(user).toLowerCase() === needle);
    return match ? stored(match) : null;
  };

  const unresolved = text.trim() !== '' && resolve(text) === null;

  return (
    <>
      <input
        id={inputId}
        type="text"
        role="combobox"
        list={listId}
        className="user-picker"
        value={text}
        placeholder={placeholder}
        autoComplete="off"
        aria-invalid={unresolved && field === 'id' ? true : undefined}
        aria-describedby={unresolved && field === 'id' ? `${inputId}-note` : undefined}
        onChange={(e) => {
          const typed = e.target.value;
          setText(typed);

          const resolved = resolve(typed);
          if (resolved !== null) {
            onChange(resolved);
            return;
          }

          // Nobody matched. A name field keeps what was typed - that is how an imported
          // name nobody registered survives an edit. An id field cannot: there is no
          // contact to point at, so the field is cleared until one is chosen.
          onChange(field === 'id' ? '' : typed);
        }}
      />
      <datalist id={listId}>
        {active.map((user) => (
          <option key={user.id} value={optionText(user)}>
            {field === 'name' ? user.email : field === 'email' ? user.name : ''}
          </option>
        ))}
      </datalist>
      {unresolved && field === 'id' ? (
        <p id={`${inputId}-note`} className="user-picker__note" role="status">
          Nobody in the directory matches that, so nobody is recorded. Pick a name from the
          list.
        </p>
      ) : null}
    </>
  );
}
