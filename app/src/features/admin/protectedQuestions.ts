/**
 * The question codes compiled C# or a portal template reads by name (AD-122). Mirrors `ChecklistGuards` in
 * `plugins/OutcomeTesting.Plugins`, which is authoritative — `al_RetireQuestion`,
 * `al_MoveQuestion` and `al_RetireSection` refuse them server-side whatever this file says.
 *
 * This copy exists only so the library does not offer a Retire control that would be
 * refused, which is the AD-041 pattern: the client shows and hides, the plug-in decides.
 * `protectedQuestions.test.ts` reads the C# and fails if the two lists drift apart.
 *
 * The reasons here are shortened for a tooltip. The server returns the full sentence.
 */
const PROTECTED: Record<string, string> = {
  'Q-GR-01': 'carries the advice quality grade, which decides the case outcome',
  'Q-GR-02': 'is the primary root cause, required on every grade but Pass',
  'Q-TAX-02': 'is the Tax check outcome, which decides whether a case goes to remediation',
  'Q-FQ-01': 'is the AQS file quality outcome and Trail Light column 10',
  'Q-FQTAX-01': 'is the Tax file quality outcome',
  'Q-FQ-02': 'is the AQS checker observation, carried into the remediation description',
  'Q-FQTAX-02': 'is the Tax checker observation, carried into the remediation description',
  'Q-FQ-03': 'is the AQS "Remedial action required?" answer, which raises remediation',
  'Q-FQTAX-03': 'is the Tax "Remedial action required?" answer, which raises remediation',
  'Q-GR-03': "is the AQS case notes, which the portal's AQS check notes card finds by this code",
  'Q-GR-04': 'is the AQS "Even Better If" note, which the AQS check notes card on the portal finds by this code',
  'Q-TAX-03': "is the Tax case notes, which the portal's Tax check notes card finds by this code",
  'Q-TAX-04': "is the Tax remedial note, which the portal's Tax check notes card finds by this code",
  'Q-TAX-05': 'is the Calculations remediation answer; a Yes raises its own remedial action',
  'Q-TAX-06': "holds the Calculations remediation comments, which become that action's wording",
  'Q-TAX-07': 'is the Taxation Narrative remediation answer; a Yes raises its own remedial action',
  'Q-TAX-08': "holds the Taxation Narrative remediation comments, which become that action's wording",
  'Q-TAX-09': 'is the Omissions remediation answer; a Yes raises its own remedial action',
  'Q-TAX-10': "holds the Omissions remediation comments, which become that action's wording",
  'Q-TAX-11': 'is the Other remediation answer; a Yes raises its own remedial action',
  'Q-TAX-12': "holds the Other remediation comments, which become that action's wording",
};

/** Codes guarded by how they start: grading counts every Q-AML… question as an AML checking point. */
const PROTECTED_PREFIXES: Record<string, string> = {
  'Q-AML': 'is an AML checking point, which grading reads by the start of its code',
};

/** Every protected code. */
export const PROTECTED_QUESTION_CODES: readonly string[] = Object.keys(PROTECTED);

/** Every protected code prefix. */
export const PROTECTED_QUESTION_PREFIXES: readonly string[] = Object.keys(PROTECTED_PREFIXES);

/**
 * Why this question cannot be retired or moved, or null when it can. The caller renders it
 * as the reason a control is absent, so it reads as a continuation of the code: "Q-GR-01
 * carries the advice quality grade…".
 */
export function protectedReason(questionCode: string | null | undefined): string | null {
  if (!questionCode) return null;

  const code = questionCode.trim().toUpperCase();
  const prefix = PROTECTED_QUESTION_PREFIXES.find((start) => code.startsWith(start));
  const reason = PROTECTED[code] ?? (prefix ? PROTECTED_PREFIXES[prefix] : undefined);
  return reason ? `${code} ${reason}.` : null;
}
