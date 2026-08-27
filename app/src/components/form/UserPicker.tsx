import { useId } from 'react';
import { useUserDirectory } from '../../hooks/useUserDirectory';
import './UserPicker.css';

interface Props {
  id?: string;
  /** Stored value. Person fields hold the user's display name (AD-029 keeps these as text). */
  value: string;
  onChange: (value: string) => void;
  /** Shown as the empty option. */
  placeholder?: string;
}

/**
 * Person selector sourced from the application user registry (al_user). Replaces free text
 * so a person field is chosen from known users rather than typed. The current value is kept
 * selectable even when it is not (yet) a registered user, so imported names are never lost
 * (AD-029: the user lookups on the case remain text). When the directory cannot load, this
 * degrades to a plain text input so editing is never blocked.
 */
export function UserPicker({ id, value, onChange, placeholder = 'Select a person' }: Props) {
  const generatedId = useId();
  const inputId = id ?? generatedId;
  const directory = useUserDirectory();

  if (directory.status !== 'ready') {
    return (
      <input
        id={inputId}
        type="text"
        className="user-picker__fallback"
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={directory.status === 'loading' ? 'Loading people…' : placeholder}
      />
    );
  }

  const active = directory.users.filter((u) => u.active);
  const knownNames = new Set(active.map((u) => u.name));
  const hasUnlistedValue = value.trim().length > 0 && !knownNames.has(value);

  return (
    <select
      id={inputId}
      className="user-picker"
      value={value}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">{placeholder}</option>
      {hasUnlistedValue ? <option value={value}>{value} (not in user registry)</option> : null}
      {active.map((user) => (
        <option key={user.id} value={user.name}>
          {user.name} — {user.email}
        </option>
      ))}
    </select>
  );
}
