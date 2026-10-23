import type { DirectoryUser } from '../../hooks/useUserDirectory';

/** What the person sees for one user, and what they type to choose them. */
export function pickerOptionText(user: DirectoryUser, field: 'name' | 'email' | 'id', withEmail: boolean): string {
  if (field === 'email') return user.email;
  if (field === 'id' || withEmail) return `${user.name} — ${user.email}`;
  return user.name;
}

/** The user whose option text is this (case and spaces ignored), or null. */
export function resolvePicker(
  users: DirectoryUser[],
  field: 'name' | 'email' | 'id',
  withEmail: boolean,
  typed: string,
): DirectoryUser | null {
  const needle = typed.trim().toLowerCase();
  if (needle === '') return null;
  return users.find((user) => pickerOptionText(user, field, withEmail).toLowerCase() === needle) ?? null;
}

/**
 * Whether the box holds half-typed text that the held value already accounts for, so a
 * re-sync from the value would only wipe what the person is typing.
 *
 * Text matching nobody is held as typed by a name or email field, and as '' by an id field.
 * Without this, an id field emptied its whole box the moment one character was deleted from
 * a chosen name: the value fell to '', and the re-sync drew '' over the text.
 */
export function keepsTypedText(
  users: DirectoryUser[],
  field: 'name' | 'email' | 'id',
  withEmail: boolean,
  text: string,
  value: string,
): boolean {
  if (text.trim() === '' || resolvePicker(users, field, withEmail, text) !== null) return false;
  return value === (field === 'id' ? '' : text);
}
