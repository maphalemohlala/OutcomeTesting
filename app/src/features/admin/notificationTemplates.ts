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
  /**
   * The subject this letter is sent with when nothing has been saved over it.
   *
   * A copy of the assembly's, kept honest by `notificationTemplates.test.ts` (F28).
   */
  subject: string;
  /** The body this letter is sent with when nothing has been saved over it. */
  body: string;
}

const TEMPLATES: Record<string, TemplateHint> = {
  ALLOCATION: {
    name: 'Case allocated',
    tokens: ['reference', 'caseLink'],
    isHtml: false,
    subject: 'Case {{reference}} has been allocated to you',
    body:
      'Case {{reference}} is now assigned to you for checking. Open it to start the review: {{caseLink}}',
  },
  'ALLOCATION-NO-LINK': {
    name: 'Case allocated (no portal link)',
    tokens: ['reference'],
    isHtml: false,
    subject: 'Case {{reference}} has been allocated to you',
    body:
      'Case {{reference}} is now assigned to you for checking. Open it in the portal to start the review.',
  },
  'REVIEW-SUBMITTED': {
    name: 'Review submitted',
    tokens: ['reference'],
    isHtml: false,
    subject: 'Review submitted on case {{reference}}',
    body:
      'The review on case {{reference}} has been submitted and is locked to further edits (FR-017).',
  },
  'REMEDIATION-PASS-WITH-ISSUES': {
    name: 'Remedial needed - pass with issues',
    tokens: ['reference', 'adviser', 'client', 'grading', 'dueText', 'caseButton'],
    isHtml: true,
    subject: 'Remedial needed - Pass with issues: {{reference}}',
    body:
      '<p>Dear {{adviser}},</p><p>{{client}} has been subject to an AQS file check and a need for remedial work has been identified due to the case receiving {{grading}}.{{dueText}}</p><p>The case summary in the portal details the remedial actions.</p><p>Please follow the link to confirm that the remedial action has been taken.</p>{{caseButton}}<p>Many thanks</p>',
  },
  'REMEDIATION-HARM': {
    name: 'Remedial needed - insufficient evidence or potential harm',
    tokens: ['reference', 'adviser', 'client', 'grading', 'dueText', 'caseButton'],
    isHtml: true,
    subject: 'Remedial needed - insufficient evidence/ potential harm: {{reference}}',
    body:
      '<p>Dear {{adviser}},</p><p>{{client}} has been subject to an AQS file check and a need for remedial work has been identified due to the case receiving {{grading}}.{{dueText}}</p><p>The case summary in the portal details the remedial actions. Please liaise with your T&amp;C Manager to move this case forward.</p><p>Please follow the link to confirm that the required remedial action has been taken.</p>{{caseButton}}<p>Many thanks</p>',
  },
  'REMEDIATION-OTHER': {
    name: 'Remediation raised (other grading)',
    tokens: ['reference', 'dueText'],
    isHtml: false,
    subject: 'Remediation required on case {{reference}}',
    body:
      'Remediation has been raised against case {{reference}} and assigned to you (BR-006).{{dueText}} Record your response against each item in the portal.',
  },
  'CASE-PASSED': {
    name: 'Case check - Pass',
    tokens: ['reference', 'adviser', 'client', 'caseButton'],
    isHtml: true,
    subject: 'Case check - Pass: {{reference}}',
    body:
      '<p>Dear {{adviser}},</p><p>{{client}} has been checked and graded a Pass.</p><p>No further action is required.</p>{{caseButton}}<p>Kind regards</p>',
  },
  'SIGNOFF-APPROVED-RECHECK': {
    name: 'Remediation approved - moving to recheck',
    tokens: ['reference', 'notes'],
    isHtml: false,
    subject: 'Remediation approved on case {{reference}}',
    body:
      'Your remediation on case {{reference}} has been approved and the case has moved on to recheck.{{notes}}',
  },
  'SIGNOFF-APPROVED-CLOSED': {
    name: 'Remediation approved - case closed',
    tokens: ['reference', 'finalOutcome', 'notes'],
    isHtml: false,
    subject: 'Remediation approved on case {{reference}}',
    body:
      'Your remediation on case {{reference}} has been approved, and the case is now closed with a final outcome of {{finalOutcome}}.{{notes}}',
  },
  'SIGNOFF-REJECTED': {
    name: 'Remediation sent back',
    tokens: ['reference', 'notes'],
    isHtml: false,
    subject: 'Remediation sent back on case {{reference}}',
    body:
      'Your remediation on case {{reference}} has been sent back for further work. The ten-working-day clock has restarted from today (OD-018).{{notes}}',
  },
  'RECHECK-DUE': {
    name: 'Final outcome owed',
    tokens: ['reference'],
    isHtml: false,
    subject: 'Case {{reference}} is waiting for its final outcome',
    body:
      'The remediation on case {{reference}} is approved and the case is now at Awaiting Recheck. It is waiting for you to record the final outcome, which is what closes it - until then it stays open and does not reach the export. Open the case\'s remediation page and use Record the final outcome.',
  },
  'SIGNOFF-DUE': {
    name: 'Sign-off due',
    tokens: ['reference'],
    isHtml: false,
    subject: 'Sign-off needed on case {{reference}}',
    body:
      'The adviser has completed every remediation action on case {{reference}}, so it is now waiting for your sign-off.',
  },
};

/**
 * The wording a letter is sent with when nothing has been saved over it (F28).
 *
 * The editor showed an empty subject and body for a letter running on its built-in copy,
 * so the page whose stated purpose is "the subject and body of every email this system
 * sends" showed neither, for exactly the letters it was sending. Anyone wanting to adjust
 * the standard wording had to retype the whole letter, because they could not see it.
 *
 * A second copy of the letters is a real cost and the repo argues against one elsewhere -
 * the registration tool LINKS the C# file rather than transcribe it. The app cannot link
 * C#, so the copy is policed instead: `notificationTemplates.test.ts` reads
 * NotificationTemplates.cs and fails if either wording moves without the other. That is the
 * same bargain this file already made for the names, the tokens and the HTML flag.
 */
export function builtInWording(code: string): { subject: string; body: string } | null {
  const hint = templateHint(code);
  return hint === null ? null : { subject: hint.subject, body: hint.body };
}

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
// The attachment marker is allowed in every letter (AD-171), so the editor must not
// report it as one this letter does not supply.
export function unknownTokens(code: string, subject: string, body: string): string[] {
  const hint = templateHint(code);
  if (!hint) return [];

  const allowed = new Set([...hint.tokens, 'completedCheck']);
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
