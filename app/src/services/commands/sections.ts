import { executeCommand, type CommandResult } from './commandClient';

/**
 * The section administration commands (AD-003, AD-123). Sections carry effective dates, an
 * optional flag and an owning team of Tax, AQS or Both; these three commands are the only
 * way the app writes any of that, because security is enforced in the plug-in and never in
 * UI code (AD-041).
 */

/** Tax team, AQS checker, or Both — the three teams a section may be owned by. */
export const OWNER_ROLE_TAX = 120910100;
export const OWNER_ROLE_AQS = 120910101;
export const OWNER_ROLE_BOTH = 120910105;

/** One question as AddSection receives it. Marshalled as JSON into a string parameter. */
export interface SectionQuestionInput {
  code: string;
  name: string;
  wording: string;
  responseType: number;
  /** Absent is mandatory (AD-019). */
  mandatory?: boolean;
  /** Absent is the position in the array. */
  displayOrder?: number;
}

export interface AddSectionInput {
  sectionCode: string;
  name: string;
  helpText?: string;
  ownerRole: number;
  displayOrder?: number;
  isOptional?: boolean;
  /** yyyy-MM-dd. Absent is today. */
  effectiveFrom?: string;
  questions?: SectionQuestionInput[];
  idempotencyKey: string;
}

export interface AddSectionOutput {
  SectionId: string;
  /** Comma-separated ids of the questions created, in order. Empty when there were none. */
  QuestionIds: string;
  AuditEventId: string;
  Conflict: boolean;
}

/**
 * Creates a section under the checklist version in force, with its questions, in one
 * transaction — so a section arrives complete rather than as an empty shell to fill in.
 *
 * The questions are sent as a JSON array in a string parameter, because Custom API
 * parameters are scalars. The server refuses a payload that is not an array: a JSON object
 * deserialises into an empty array without error, which would otherwise create the section
 * with no questions and report success.
 */
export function addSection(input: AddSectionInput): Promise<CommandResult<AddSectionOutput>> {
  const body: Record<string, unknown> = {
    SectionCode: input.sectionCode,
    Name: input.name,
    OwnerRole: String(input.ownerRole),
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.helpText !== undefined) body.HelpText = input.helpText;
  if (input.displayOrder !== undefined) body.DisplayOrder = String(input.displayOrder);
  if (input.isOptional !== undefined) body.IsOptional = input.isOptional ? 'true' : 'false';
  if (input.effectiveFrom !== undefined) body.EffectiveFrom = input.effectiveFrom;
  if (input.questions !== undefined && input.questions.length > 0) {
    body.Questions = JSON.stringify(input.questions);
  }
  return executeCommand<AddSectionOutput>('al_AddSection', body);
}

export interface RetireSectionInput {
  sectionId: string;
  /** yyyy-MM-dd. Absent is today. */
  effectiveTo?: string;
  reason: string;
  idempotencyKey: string;
}

export interface RetireSectionOutput {
  SectionId: string;
  AuditEventId: string;
  Conflict: boolean;
}

/**
 * Dates out a section, which is what removes its questions from the form and the submit
 * gate. Their own versions are left alone, so the answers they hold keep resolving
 * (AD-091). A section holding one of the eight load-bearing codes is refused.
 */
export function retireSection(
  input: RetireSectionInput,
): Promise<CommandResult<RetireSectionOutput>> {
  const body: Record<string, unknown> = {
    SectionId: input.sectionId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.effectiveTo !== undefined) body.EffectiveTo = input.effectiveTo;
  return executeCommand<RetireSectionOutput>('al_RetireSection', body);
}

export interface UpdateSectionInput {
  sectionId: string;
  name?: string;
  helpText?: string;
  /** Retroactive: owner role is not versioned, so this reaches submitted reviews. */
  ownerRole?: number;
  displayOrder?: number;
  /** Takes effect on every unsubmitted review immediately; there is no date on the flag. */
  isOptional?: boolean;
  reason: string;
  idempotencyKey: string;
}

export interface UpdateSectionOutput {
  SectionId: string;
  /** The before and after of each changed field. Empty when nothing changed. */
  Changes: string;
  /** Empty when nothing changed — a no-op writes no Audit Event. */
  AuditEventId: string;
  Conflict: boolean;
}

/**
 * Amends a section in place, recording the before and after of each changed field on an
 * immutable Audit Event (AD-043). Sections are not versioned, so that trail is what
 * preserves the history instead.
 *
 * A call that changes nothing writes nothing and returns an empty `AuditEventId`, which
 * matters because the editor calls this whenever it is opened and closed.
 */
export function updateSection(
  input: UpdateSectionInput,
): Promise<CommandResult<UpdateSectionOutput>> {
  const body: Record<string, unknown> = {
    SectionId: input.sectionId,
    Reason: input.reason,
    IdempotencyKey: input.idempotencyKey,
  };
  if (input.name !== undefined) body.Name = input.name;
  if (input.helpText !== undefined) body.HelpText = input.helpText;
  if (input.ownerRole !== undefined) body.OwnerRole = String(input.ownerRole);
  if (input.displayOrder !== undefined) body.DisplayOrder = String(input.displayOrder);
  if (input.isOptional !== undefined) body.IsOptional = input.isOptional ? 'true' : 'false';
  return executeCommand<UpdateSectionOutput>('al_UpdateSection', body);
}
