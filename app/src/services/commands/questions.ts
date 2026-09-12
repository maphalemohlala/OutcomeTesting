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
  displayOrder?: number;
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
  if (input.displayOrder !== undefined) body.DisplayOrder = String(input.displayOrder);
  return executeCommand<RetireAndSucceedQuestionOutput>('al_RetireAndSucceedQuestion', body);
}

/**
 * AddQuestion command (AD-003, AD-122). Creates a question and its first version in a
 * section of the checklist version in force. `effectiveFrom` may be today or later and
 * never the past: a question cannot retrospectively have been owed by a review that has
 * already been answered.
 */

export interface AddQuestionInput {
  sectionId: string;
  questionCode: string;
  name: string;
  wording: string;
  responseType: number;
  mandatory?: boolean;
  displayOrder?: number;
  /** yyyy-MM-dd. Absent is today. */
  effectiveFrom?: string;
  idempotencyKey: string;
}

export interface AddQuestionOutput {
  QuestionId: string;
  VersionId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function addQuestion(input: AddQuestionInput): Promise<CommandResult<AddQuestionOutput>> {
  const body: Record<string, unknown> = {
    SectionId: input.sectionId,
    QuestionCode: input.questionCode,
    Name: input.name,
    Wording: input.wording,
    ResponseType: String(input.responseType),
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.mandatory !== undefined) body.Mandatory = input.mandatory ? 'true' : 'false';
  if (input.displayOrder !== undefined) body.DisplayOrder = String(input.displayOrder);
  if (input.effectiveFrom !== undefined) body.EffectiveFrom = input.effectiveFrom;
  return executeCommand<AddQuestionOutput>('al_AddQuestion', body);
}

/**
 * RetireQuestion command (AD-003, AD-122). Dates out the current version and creates no
 * successor, so the question stops being asked while its answers keep resolving. The eight
 * codes compiled logic depends on are refused server-side.
 */

export interface RetireQuestionInput {
  questionId: string;
  /** yyyy-MM-dd. Absent is today. */
  effectiveTo?: string;
  reason: string;
  idempotencyKey: string;
}

export interface RetireQuestionOutput {
  QuestionId: string;
  RetiredVersionId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function retireQuestion(
  input: RetireQuestionInput,
): Promise<CommandResult<RetireQuestionOutput>> {
  const body: Record<string, unknown> = {
    QuestionId: input.questionId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.effectiveTo !== undefined) body.EffectiveTo = input.effectiveTo;
  return executeCommand<RetireQuestionOutput>('al_RetireQuestion', body);
}

/**
 * MoveQuestion command (AD-003, AD-122). A retire plus an add in one transaction, never a
 * lookup update: al_sectionid is not versioned, so changing it in place would re-file every
 * answer ever recorded under a section it was never answered in. The question needs a new
 * code in its new section, because codes are unique and never freed.
 */

export interface MoveQuestionInput {
  questionId: string;
  targetSectionId: string;
  newQuestionCode: string;
  /** yyyy-MM-dd for the new question. Absent is today. */
  effectiveFrom?: string;
  reason: string;
  idempotencyKey: string;
}

export interface MoveQuestionOutput {
  RetiredVersionId: string;
  NewQuestionId: string;
  NewVersionId: string;
  AuditEventId: string;
  Conflict: boolean;
}

export function moveQuestion(input: MoveQuestionInput): Promise<CommandResult<MoveQuestionOutput>> {
  const body: Record<string, unknown> = {
    QuestionId: input.questionId,
    TargetSectionId: input.targetSectionId,
    NewQuestionCode: input.newQuestionCode,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.effectiveFrom !== undefined) body.EffectiveFrom = input.effectiveFrom;
  return executeCommand<MoveQuestionOutput>('al_MoveQuestion', body);
}
