/** Two letters for an avatar or the product mark: first letters of two words, or of one word. */
export function initials(name: string): string {
  const words = name.split(/[\s.@]+/).filter(Boolean);
  return (words.length > 1 ? words[0][0] + words[1][0] : name.slice(0, 2)).toUpperCase();
}

/** The role named under the user: Administrator when held, else the first role without its portal prefix. */
export function roleLabel(roles: readonly string[]): string {
  if (roles.includes('Administrators')) return 'Administrator';
  return roles[0]?.replace(/^AL Portal - /, '') ?? 'No role yet';
}
