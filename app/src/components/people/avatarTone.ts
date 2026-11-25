const TONES = 5;

/** The same name always lands on the same tint, so a person is recognisable across pages. */
export function avatarTone(name: string): number {
  let sum = 0;
  for (const ch of name.trim().toLowerCase()) sum = (sum * 31 + ch.charCodeAt(0)) % 9973;
  return sum % TONES;
}
