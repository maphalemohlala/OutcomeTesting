import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';

export interface CaseOutcome {
  id: string;
  reference: string;
  /** The grade first recorded. Never overwritten — BR-007 keeps both. */
  initialOutcome: string;
  /** Set only once a regrade or recheck has run; null while the initial grade stands. */
  finalOutcome: string | null;
  regradeReason: string | null;
  regradedOn: string | null;
  finalisedOn: string | null;
  reviewInstance: string | null;
  /**
   * Optimistic-concurrency token passed back as ExpectedRowVersion, so a regrade based on
   * a stale read is refused rather than silently overwriting someone else's correction.
   */
  rowVersion: string | null;
}

function date(value: string | undefined): string | null {
  if (!value) return null;
  const time = new Date(value).getTime();
  if (Number.isNaN(time)) return null;
  return new Date(time).toLocaleDateString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
  });
}

/**
 * Maps one al_outcome row to the shape the recheck screen reads.
 *
 * Labels come from the generated maps rather than the `*name` formatted values, because
 * Dataverse omits the formatted value on some reads and a blank grade on a regrade screen
 * is worse than a wrong one — it invites a correction against a row the user cannot see.
 *
 * `al_finaloutcome` and `al_initialoutcome` are separate columns holding separate option
 * sets (BR-007), so the final grade is never derived from the initial one.
 */
export function toOutcome(record: Al_outcomes): CaseOutcome {
  const final =
    record.al_finaloutcomename ??
    (record.al_finaloutcome === undefined
      ? undefined
      : Al_outcomesal_finaloutcome[record.al_finaloutcome]);

  return {
    id: record.al_outcomeid,
    reference: record.al_name?.trim() || record.al_outcomecode,
    initialOutcome:
      record.al_initialoutcomename ??
      Al_outcomesal_initialoutcome[record.al_initialoutcome] ??
      '—',
    finalOutcome: final ?? null,
    regradeReason: record.al_regradereason?.trim() || null,
    regradedOn: date(record.al_regradedon),
    finalisedOn: date(record.al_finalisedon),
    reviewInstance: record.al_reviewinstanceidname?.trim() || null,
    rowVersion: record.versionnumber === undefined ? null : String(record.versionnumber),
  };
}
