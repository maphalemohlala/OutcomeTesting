import { useId, useMemo, useState } from 'react';
import { useUserDirectory, type DirectoryUser } from '../../hooks/useUserDirectory';
import './UserPicker.css';

interface Props {
  id?: string;
  /** Stored value. What it means depends on `field`. */
  value: string;
  onChange: (value: string) => void;
  /** Shown as the empty option. */
  placeholder?: string;
  /**
   * Which registry column the stored value is. A person field on a case holds the display
   * name (AD-029); a role assignment is keyed on work email (AD-010); an accountability
   * lookup holds the contact's id. All three are a choice of one person from the same
   * registry, so all three use this control and differ only in what they store. Every
   * option is labelled with the name and the email whichever it is, so the three forms read
   * identically to the person choosing.
   */
  field?: 'name' | 'email' | 'id';
}

/**
 * Person selector sourced from the application user registry (contact). Replaces free text
 * so a person field is chosen from known users rather than typed. The current value is kept
 * selectable even when it is not (yet) a registered user, so imported names are never lost
 * (AD-029: the user lookups on the case remain text). When the directory cannot load, this
 * degrades to a plain text input so editing is never blocked.
 *
 * Searchable (project owner, 2026-09-19). A filter box above the list narrows it by name or
 * email as you type; the list itself stays a native select, so keyboard and screen-reader
 * behaviour is the platform's own rather than something reimplemented. A datalist combobox
 * was the other option and was not taken: it stores the text a person typed, which cannot
 * carry a contact's id, and it silently accepts a name that matches nobody.
 *
 * The chosen person is never filtered out of the list. Narrowing the options under a
 * selection would blank the control and quietly change what is about to be saved.
 */
export function UserPicker({
  id,
  value,
  onChange,
  placeholder = 'Select a person',
  field = 'name',
}: Props) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const filterId = `${inputId}-filter`;
  const directory = useUserDirectory();
  const [filter, setFilter] = useState('');

  const storedValue = (user: DirectoryUser) =>
    field === 'email' ? user.email : field === 'id' ? user.id : user.name;

  const active = useMemo(
    () => (directory.status === 'ready' ? directory.users.filter((u) => u.active) : []),
    [directory],
  );

  const matches = useMemo(() => {
    const needle = filter.trim().toLowerCase();
    if (needle === '') return active;
    return active.filter(
      (user) =>
        user.name.toLowerCase().includes(needle) || user.email.toLowerCase().includes(needle),
    );
  }, [active, filter]);

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

  const known = new Set(active.map(storedValue));
  const hasUnlistedValue = value.trim().length > 0 && !known.has(value);

  // The selection survives the filter: a narrowed list that excludes the current value
  // would leave the select showing nothing and change what a save would write.
  const selected = active.find((user) => storedValue(user) === value);
  const shown =
    selected && !matches.includes(selected) ? [selected, ...matches] : matches;

  return (
    <div className="user-picker__group">
      <input
        id={filterId}
        type="search"
        className="user-picker__filter"
        value={filter}
        onChange={(e) => setFilter(e.target.value)}
        placeholder="Search by name or email"
        aria-label="Search people"
        aria-controls={inputId}
        autoComplete="off"
      />
      <select
        id={inputId}
        className="user-picker"
        value={value}
        onChange={(e) => onChange(e.target.value)}
      >
        <option value="">{placeholder}</option>
        {hasUnlistedValue ? (
          <option value={value}>{value} (not in the people directory)</option>
        ) : null}
        {shown.map((user) => (
          <option key={user.id} value={storedValue(user)}>
            {user.name} — {user.email}
          </option>
        ))}
      </select>
      <p className="user-picker__count" role="status">
        {filter.trim() === ''
          ? `${active.length} ${active.length === 1 ? 'person' : 'people'}`
          : `${matches.length} of ${active.length} match “${filter.trim()}”`}
      </p>
    </div>
  );
}
