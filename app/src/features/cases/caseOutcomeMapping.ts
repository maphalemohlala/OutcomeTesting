import {
  Al_outcomesal_finaloutcome,
  Al_outcomesal_initialoutcome,
  type Al_outcomes,
} from '../../generated/models/Al_outcomesModel';
import { date } from '../../lib/format';
import { lookupLabel } from './lookupLabel';
import { recordedFlags, type AccountabilityFlags } from './failAccountability';

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
  /** Whether a regrade has run on this outcome. */
  regraded: boolean;
  /**
   * Who has been recorded as carrying a fail, exactly as the row holds it (item 8,
   * 2026-09-19). Four falses means nobody has said yet, and the export derives the pair -
   * effectiveAccountability is what turns that into what the extract will actually carry.
   */
  accountability: AccountabilityFlags;
  /**
   * The contact named as carrying each fail, where someone other than the case's own
   * adviser or paraplanner was chosen. Null means the extract uses the person the case names.
   */
  fqAccountable: { id: string; name: string } | null;
  aqAccountable: { id: string; name: string } | null;
  /** The administrator carrying the File Quality fail (requirements log row 10); never exported. */
  fqAdministrator?: { id: string; name: string } | null;
  /**
   * Optimistic-concurrency token passed back as ExpectedRowVersion, so a regrade based on
   * a stale read is refused rather than silently overwriting someone else's correction.
   */
  rowVersion: string | null;
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
    (record.al_finaloutcome == null ? undefined : Al_outcomesal_finaloutcome[record.al_finaloutcome]);

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
    reviewInstance: lookupLabel(record, 'al_reviewinstanceid', record.al_reviewinstanceidname),
    regraded: Boolean(record.al_regradedon),
    accountability: recordedFlags(record),
    fqAccountable: namedContact(
      record._al_fqaccountablecontactid_value,
      lookupLabel(record, 'al_fqaccountablecontactid', record.al_fqaccountablecontactidname),
    ),
    aqAccountable: namedContact(
      record._al_aqaccountablecontactid_value,
      lookupLabel(record, 'al_aqaccountablecontactid', record.al_aqaccountablecontactidname),
    ),
    fqAdministrator: namedContact(
      record._al_fqadministratorcontactid_value,
      lookupLabel(record, 'al_fqadministratorcontactid', record.al_fqadministratorcontactidname),
    ),
    rowVersion: record.versionnumber != null ? String(record.versionnumber) : null,
  };
}

/** A lookup as the accountability editor needs it: the id to send back, and a name to show. */
function namedContact(id?: string, name?: string | null): { id: string; name: string } | null {
  if (!id) return null;
  return { id, name: name?.trim() || 'Someone not in the directory' };
}
