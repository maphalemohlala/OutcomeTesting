/**
 * People on a case are identified by EMAIL, never by name (owner, 2026-10-02: two people can
 * share a name). The name is a label written beside the email. The server applies the same
 * rules in CasePeople.cs; these spare a round trip.
 */

const EMAIL_SHAPE = /^[^@\s]+@[^@\s]+\.[^@\s]+$/;

export function isEmail(value: string | null | undefined): boolean {
  const trimmed = (value ?? '').trim();
  return trimmed !== '' && EMAIL_SHAPE.test(trimmed);
}

export const PERSON_PAIRS = [
  { name: 'al_advisername', email: 'al_adviseremail', who: 'adviser' },
  { name: 'al_paraplanner', email: 'al_paraplanneremail', who: 'paraplanner' },
] as const;

export type PersonForm = Record<(typeof PERSON_PAIRS)[number]['name' | 'email'], string>;

/** The changed fields, with each touched person's other half added so the two travel together. */
export function withPersonPairs(
  changed: Record<string, string>,
  form: PersonForm,
): Record<string, string> {
  const out = { ...changed };
  for (const pair of PERSON_PAIRS) {
    if (pair.name in changed || pair.email in changed) {
      out[pair.name] = form[pair.name] ?? '';
      out[pair.email] = form[pair.email] ?? '';
    }
  }
  return out;
}

/** What stops the save, for the people this save touches only. */
export function personRefusals(changed: Record<string, string>, form: PersonForm): string[] {
  const refusals: string[] = [];
  for (const pair of PERSON_PAIRS) {
    if (!(pair.name in changed) && !(pair.email in changed)) continue;
    const name = (form[pair.name] ?? '').trim();
    const email = (form[pair.email] ?? '').trim();
    if (name !== '' && email === '') {
      refusals.push(`Give the ${pair.who}'s email as well as their name.`);
    } else if (email !== '' && !isEmail(email)) {
      refusals.push(`The ${pair.who}'s email "${email}" is not an email address.`);
    }
  }
  return refusals;
}
