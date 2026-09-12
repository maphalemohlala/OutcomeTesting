/**
 * The question codes compiled C# reads by name (AD-122). Mirrors `ChecklistGuards` in
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
  'Q-TAX-02': 'is the Tax check outcome, which decides whether a case goes to remediation',
  'Q-FQ-01': 'is the AQS file quality outcome and Trail Light column 10',
  'Q-FQTAX-01': 'is the Tax file quality outcome',
  'Q-FQ-02': 'is the AQS checker observation, carried into the remediation description',
  'Q-FQTAX-02': 'is the Tax checker observation, carried into the remediation description',
  'Q-FQ-03': 'is the AQS "Remedial action required?" answer, which raises remediation',
  'Q-FQTAX-03': 'is the Tax "Remedial action required?" answer, which raises remediation',
};

/** Every protected code. */
export const PROTECTED_QUESTION_CODES: readonly string[] = Object.keys(PROTECTED);

/**
 * Why this question cannot be retired or moved, or null when it can. The caller renders it
 * as the reason a control is absent, so it reads as a continuation of the code: "Q-GR-01
 * carries the advice quality grade…".
 */
export function protectedReason(questionCode: string | null | undefined): string | null {
  if (!questionCode) return null;

  const code = questionCode.trim().toUpperCase();
  const reason = PROTECTED[code];
  return reason ? `${code} ${reason}.` : null;
}
