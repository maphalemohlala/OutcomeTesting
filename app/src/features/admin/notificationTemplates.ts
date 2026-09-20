/**
 * The letters this solution sends, and the tokens each one supplies.
 *
 * Mirrors `NotificationTemplates` in `plugins/OutcomeTesting.Plugins`, which is
 * authoritative — `NotificationTemplateGuardPlugin` refuses a bad template server-side
 * whatever this file says, and the table is reachable from the Web API where this file is
 * not involved at all.
 *
 * This copy exists so the editor can name the tokens a letter supplies and catch a mistake
 * before a round trip, which is the AD-041 pattern: the client shows and hides, the plug-in
 * decides. `notificationTemplates.test.ts` reads the C# and fails if the two drift apart.
 */
export interface TemplateHint {
  /** What an administrator sees in the list. */
  name: string;
  /** The tokens this letter supplies, without braces. */
  tokens: readonly string[];
  /** True when the body is markup rather than plain text. */
  isHtml: boolean;
}

const TEMPLATES: Record<string, TemplateHint> = {
  ALLOCATION: {
    name: 'Case allocated',
    tokens: ['reference', 'caseLink'],
    isHtml: false,
  },
  'ALLOCATION-NO-LINK': {
    name: 'Case allocated (no portal link)',
    tokens: ['reference'],
    isHtml: false,
  },
  'REVIEW-SUBMITTED': {
    name: 'Review submitted',
    tokens: ['reference'],
    isHtml: false,
  },
  'REMEDIATION-PASS-WITH-ISSUES': {
    name: 'Remedial needed - pass with issues',
    tokens: ['reference', 'adviser', 'client', 'grading', 'dueText', 'caseButton'],
    isHtml: true,
  },
  'REMEDIATION-HARM': {
    name: 'Remedial needed - insufficient evidence or potential harm',
    tokens: ['reference', 'adviser', 'client', 'grading', 'dueText', 'caseButton'],
    isHtml: true,
  },
  'REMEDIATION-OTHER': {
    name: 'Remediation raised (other grading)',
    tokens: ['reference', 'dueText'],
    isHtml: false,
  },
  'CASE-PASSED': {
    name: 'Case check - Pass',
    tokens: ['reference', 'adviser', 'client', 'caseButton'],
    isHtml: true,
  },
  'SIGNOFF-APPROVED-RECHECK': {
    name: 'Remediation approved - moving to recheck',
    tokens: ['reference', 'notes'],
    isHtml: false,
  },
  'SIGNOFF-APPROVED-CLOSED': {
    name: 'Remediation approved - case closed',
    tokens: ['reference', 'finalOutcome', 'notes'],
    isHtml: false,
  },
  'SIGNOFF-REJECTED': {
    name: 'Remediation sent back',
    tokens: ['reference', 'notes'],
    isHtml: false,
  },
  'RECHECK-DUE': {
    name: 'Final outcome owed',
    tokens: ['reference'],
    isHtml: false,
  },
  'SIGNOFF-DUE': {
    name: 'Sign-off due',
    tokens: ['reference'],
    isHtml: false,
  },
};

/** Every template code, in the order the C# declares them. */
export const TEMPLATE_CODES: readonly string[] = Object.keys(TEMPLATES);

/** What is known about one letter, or null when the code is not one of ours. */
export function templateHint(code: string): TemplateHint | null {
  return TEMPLATES[code?.trim()] ?? null;
}

/**
 * The tokens used in this wording that the letter does not supply.
 *
 * Advisory. The server refuses the same thing with a fuller sentence; this exists so the
 * editor can say so before a round trip, and so an administrator is not left guessing at a
 * token list they cannot otherwise see.
 */
export function unknownTokens(code: string, subject: string, body: string): string[] {
  const hint = templateHint(code);
  if (!hint) return [];

  const allowed = new Set(hint.tokens);
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
