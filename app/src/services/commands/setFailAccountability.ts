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
    IdempotencyKey: input.idempotencyKey,
  });
}
