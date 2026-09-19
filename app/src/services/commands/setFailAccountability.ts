import { executeCommand, type CommandResult } from './commandClient';

/**
 * SetFailAccountability command (item 8, 2026-09-19). Records who carries a fail, against
 * the outcome rather than the case.
 *
 * The Custom API has existed since 2026-09-05 and nothing called it: every outcome row in
 * DEV therefore holds four falses, and the export has been deriving the pair from the
 * people the case names (paraplanner -> File Quality, adviser -> Advice Quality). This is
 * the route by which someone overrides that default.
 *
 * The command refuses a case with no fail to attribute. Accountability describes a fail,
 * and recording one against a clean case would put a name in a Trail Light column that
 * AD-039 only ever fills for a fail.
 *
 * Deliberately no client-side copy of "has the file quality failed?". The server answers it
 * with FileQuality.Resolve, which reads whichever of Q-FQ-01 and Q-FQTAX-01 was answered;
 * a second implementation here could disagree, and its own comment records why that matters
 * - "answering it two different ways would let the command refuse a fail the export had
 * already attributed". So the refusal is surfaced rather than predicted.
 */

export interface SetFailAccountabilityInput {
  /** Id of the al_outcome, not the case. */
  outcomeId: string;
  fqAdviser: boolean;
  fqParaplanner: boolean;
  aqAdviser: boolean;
  aqParaplanner: boolean;
  /**
   * The contact carrying the File Quality fail, where it is someone other than the adviser
   * or paraplanner the case names. Empty clears it and puts the case's own person back in
   * the extract. The flags above still say which of AD-039's two slots they fill.
   */
  fqContactId?: string;
  /** The same for Advice Quality. */
  aqContactId?: string;
  /** Stable idempotency key for this intent; reuse across retries. */
  idempotencyKey: string;
}

export interface SetFailAccountabilityOutput {
  Status: string;
  AuditEventId: string;
}

export function setFailAccountability(
  input: SetFailAccountabilityInput,
): Promise<CommandResult<SetFailAccountabilityOutput>> {
  return executeCommand<SetFailAccountabilityOutput>('al_SetFailAccountability', {
    TargetId: input.outcomeId,
    FqAdviser: input.fqAdviser,
    FqParaplanner: input.fqParaplanner,
    AqAdviser: input.aqAdviser,
    AqParaplanner: input.aqParaplanner,
    // Always sent, including as empty: the command writes both lookups on every save, so
    // clearing a named person here is what puts the case's own back in the extract.
    FqContactId: input.fqContactId ?? '',
    AqContactId: input.aqContactId ?? '',
    IdempotencyKey: input.idempotencyKey,
  });
}
