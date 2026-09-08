import { useId } from 'react';
import { useUserDirectory, type DirectoryUser } from '../../hooks/useUserDirectory';
import './UserPicker.css';

interface Props {
  id?: string;
  /** Stored value. Person fields hold the user's display name (AD-029 keeps these as text). */
  value: string;
  onChange: (value: string) => void;
  /** Shown as the empty option. */
  placeholder?: string;
  /**
   * Which registry column the stored value is. A person field on a case holds the display
   * name (AD-029); a role assignment is keyed on work email (AD-010). Both are a choice of
   * one person from the same registry, so both use this control and differ only in what
   * they store. Every option is labelled with the name and the email either way, so the
   * two forms read identically to the person choosing.
   */
  field?: 'name' | 'email';
}

/**
 * Person selector sourced from the application user registry (contact). Replaces free text
 * so a person field is chosen from known users rather than typed. The current value is kept
 * selectable even when it is not (yet) a registered user, so imported names are never lost
 * (AD-029: the user lookups on the case remain text). When the directory cannot load, this
 * degrades to a plain text input so editing is never blocked.
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
  const directory = useUserDirectory();
  const storedValue = (user: DirectoryUser) => (field === 'email' ? user.email : user.name);

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

  const active = directory.users.filter((u) => u.active);
  const known = new Set(active.map(storedValue));
  const hasUnlistedValue = value.trim().length > 0 && !known.has(value);

  return (
    <select
      id={inputId}
      className="user-picker"
      value={value}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">{placeholder}</option>
      {hasUnlistedValue ? <option value={value}>{value} (not in the people directory)</option> : null}
      {active.map((user) => (
        <option key={user.id} value={storedValue(user)}>
          {user.name} — {user.email}
        </option>
      ))}
    </select>
  );
}
