import { executeCommand, type CommandResult } from './commandClient';

/**
 * AmendOutcome command (requirements log row 39). Amends the File and/or Advice grade a check
 * first gave a case, at any stage, for an appeal. The case does not move. The server checks the
 * case.amendoutcome capability and keeps the grades before and after, and the reason, on an
 * audit event the case's History lists.
 */
export const FILE_GRADES = ['Pass', 'Fail'] as const;
export const ADVICE_GRADES = ['Pass', 'Pass with issues', 'Insufficient evidence', 'Potential harm'] as const;

export interface AmendOutcomeInput {
  caseId: string;
  /** Empty leaves the grade as it is. */
  fileGrade: string;
  adviceGrade: string;
  reason: string;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface AmendOutcomeOutput {
  Status: string;
  Summary: string;
  AuditEventId: string;
}

export function amendOutcome(input: AmendOutcomeInput): Promise<CommandResult<AmendOutcomeOutput>> {
  return executeCommand<AmendOutcomeOutput>('al_AmendOutcome', {
    TargetId: input.caseId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
    FileGrade: input.fileGrade,
    AdviceGrade: input.adviceGrade,
  });
}
