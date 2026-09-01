import { executeCommand, type CommandResult } from './commandClient';

/**
 * RetireAndSucceedQuestion command (AD-003, AD-004, FR-030/FR-031). Published checklist
 * content is immutable, so an edit retires the current question version and creates a
 * successor with new wording; submitted reviews keep the frozen version (BR-013). The
 * server-side Custom API enforces the caller and writes an immutable Audit Event.
 */

export interface RetireAndSucceedQuestionInput {
  questionId: string;
  newWording: string;
  responseType?: number;
  mandatory?: boolean;
  idempotencyKey: string;
}

export interface RetireAndSucceedQuestionOutput {
  NewVersionId: string;
  VersionNumber: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function retireAndSucceedQuestion(
  input: RetireAndSucceedQuestionInput,
): Promise<CommandResult<RetireAndSucceedQuestionOutput>> {
  const body: Record<string, unknown> = {
    QuestionId: input.questionId,
    NewWording: input.newWording,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.responseType !== undefined) body.ResponseType = String(input.responseType);
  if (input.mandatory !== undefined) body.Mandatory = input.mandatory ? 'true' : 'false';
  return executeCommand<RetireAndSucceedQuestionOutput>('al_RetireAndSucceedQuestion', body);
}
