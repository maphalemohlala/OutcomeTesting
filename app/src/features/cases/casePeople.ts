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

/**
 * True when a rename the picker could not resolve to anyone is about to leave a STALE email
 * behind (CaseEditPanel, F3 of the 2026-10-04 final review). The picker keeps whatever is
 * typed when it matches nobody (AD-029: an imported name nobody registered must survive), but
 * it does not clear the email field - so typing over "Adam Strumidlo" with a new name the
 * picker cannot resolve would otherwise save with Adam's OLD email still attached to a new
 * name. Only true while the email still reads as it was saved; an email the user typed
 * themselves is never touched.
 */
export function staleEmailAfterRename(
  savedName: string,
  savedEmail: string,
  typedName: string,
  currentEmail: string,
): boolean {
  const renamed = typedName.trim().toLowerCase() !== (savedName ?? '').trim().toLowerCase();
  return renamed && currentEmail === savedEmail;
}

/**
 * The notice for an email two or more active directory people hold (spec §1, F4 of the
 * 2026-10-04 final review, "two contacts share an email means a duplicate onboarding to clean
 * up"): nobody can be chosen, because none of the two is more right than the other. Null
 * while the field is blank or held by at most one active person.
 */
export function sharedEmailNote(email: string, activeEmails: string[]): string | null {
  const trimmed = (email ?? '').trim();
  if (trimmed === '') return null;
  const needle = trimmed.toLowerCase();
  const holders = activeEmails.filter((e) => (e ?? '').trim().toLowerCase() === needle).length;
  return holders >= 2
    ? `Two people in the directory share the email ${trimmed}, so nobody can be chosen. Remove the duplicate.`
    : null;
}

/**
 * The non-blocking note for an email field nobody active in the directory holds, or that two
 * of them hold (spec §1, F3/F4 of the 2026-10-04 final review): remediation on this case has
 * nobody to reach until the email is corrected, the duplicate is removed, or the right person
 * is added. Null while the field is blank, or while exactly one active person holds it, so a
 * legacy case with no email yet is not flagged.
 */
export function emailDirectoryNote(email: string, activeEmails: string[]): string | null {
  const shared = sharedEmailNote(email, activeEmails);
  if (shared) return shared;

  const trimmed = (email ?? '').trim();
  if (trimmed === '') return null;
  const needle = trimmed.toLowerCase();
  const matched = activeEmails.some((e) => (e ?? '').trim().toLowerCase() === needle);
  return matched
    ? null
    : 'No active person in the directory has this email, so nobody will be able to answer '
      + "this case's remediation until they are added.";
}
