/**
 * Which event sends a letter, and who receives it (AD-168).
 *
 * Mirrors `NotificationOutbox` and `NotificationRecipients` in
 * `plugins/OutcomeTesting.Plugins`, which are authoritative:
 * `NotificationTemplateGuardPlugin` refuses a bad row server-side whatever this file says,
 * and the table is reachable from the Web API where this file is not involved at all.
 *
 * This copy exists so the editor can offer the choices and name the tokens before a round
 * trip, which is the AD-041 pattern — the client shows and hides, the plug-in decides.
 * `notificationRouting.test.ts` reads the C# and fails if the two drift apart.
 */

export interface Choice {
  value: number;
  label: string;
}

/** Every event a letter can be attached to, in the order they occur on a case. */
export const TRIGGER_EVENTS: readonly Choice[] = [
  { value: 120910800, label: 'Allocation' },
  { value: 120910801, label: 'Review submitted' },
  { value: 120910802, label: 'Remediation assigned' },
  { value: 120910803, label: 'Sign-off approved' },
  { value: 120910804, label: 'Sign-off rejected' },
  { value: 120910805, label: 'Case passed' },
  { value: 120910806, label: 'Recheck due' },
  { value: 120910807, label: 'Sign-off due' },
];

/** The five people a case can always be asked for. */
export const RECIPIENT_KINDS: readonly Choice[] = [
  { value: 120910810, label: 'Adviser' },
  { value: 120910811, label: 'T&C Manager' },
  { value: 120910812, label: 'Para-planner' },
  { value: 120910813, label: 'Checker' },
  { value: 120910814, label: 'A named contact' },
];

/** The kind that needs a contact naming alongside it. */
export const KIND_CONTACT = 120910814;

/**
 * The tokens a letter of your own may use: everything a case can answer whatever raised it.
 *
 * Deliberately narrower than a built-in letter's set. Things like a grade or a final outcome
 * exist only at the moment one particular event fires, so a letter attached to an arbitrary
 * event has no claim on them — it would read correctly on this screen and arrive with gaps.
 */
export const CUSTOM_TOKENS: readonly string[] = [
  'reference',
  'adviser',
  'client',
  'caseLink',
  'caseButton',
];

export function eventLabel(value: number | null): string | null {
  if (value === null) return null;
  return TRIGGER_EVENTS.find((e) => e.value === value)?.label ?? null;
}

export function recipientLabel(value: number | null): string | null {
  if (value === null) return null;
  return RECIPIENT_KINDS.find((r) => r.value === value)?.label ?? null;
}

/**
 * The tokens used in this wording that a letter of your own cannot supply.
 *
 * The same scan the plug-in does, against the same list. Advisory: the refusal that counts
 * is the server's, and it is what the page shows on a failed save.
 */
export function unknownCustomTokens(subject: string, body: string): string[] {
  const allowed = new Set(CUSTOM_TOKENS);
  const offenders: string[] = [];
  const text = `${subject ?? ''} ${body ?? ''}`;

  for (const match of text.matchAll(/\{\{([^}]*)\}\}/g)) {
    const name = match[1].trim();
    if (!allowed.has(name) && !offenders.includes(name)) {
      offenders.push(name);
    }
  }

  return offenders;
}

/**
 * A code an administrator typed, normalised the way the plug-in stores one.
 *
 * Upper case with spaces as hyphens, because the code is the table's alternate key and is
 * matched exactly: "tell the manager" and "TELL-THE-MANAGER" must not become two rows that
 * look like one.
 */
export function normaliseCode(code: string): string {
  return code
    .trim()
    .toUpperCase()
    .replace(/[^A-Z0-9-]+/g, '-')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}
