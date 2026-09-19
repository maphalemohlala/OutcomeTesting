import type { Al_outcomes } from '../../generated/models/Al_outcomesModel';

/**
 * Who carries a fail, and whether anyone has said so yet (item 8, 2026-09-19).
 *
 * The client-side mirror of GenerateExportPlugin.IsAccountable. The four flags feed Trail
 * Light columns 11-14 and 17-20 (AD-039), so this page and the export must agree about what
 * a row means - the same reason ImportRules and caseUpload are kept in step.
 *
 * The rule, from the project owner on 2026-09-16: a recorded judgement wins outright, and
 * an untouched row derives from the people the case already names. The paraplanner compiles
 * the file, so a File Quality fail is theirs; the adviser gives the advice, so an Advice
 * Quality fail is theirs.
 *
 * "Untouched" is all four false. That is "nobody has said yet", not "nobody is
 * responsible", which is why a deliberate nobody cannot be expressed - and could not be
 * before either, the retired OD-024 gate having refused a row that named no one.
 */

export interface AccountabilityFlags {
  fqAdviser: boolean;
  fqParaplanner: boolean;
  aqAdviser: boolean;
  aqParaplanner: boolean;
}

/** Where the flags on screen came from: someone's judgement, or the default. */
export type AccountabilitySource = 'recorded' | 'derived';

export const NOBODY: AccountabilityFlags = {
  fqAdviser: false,
  fqParaplanner: false,
  aqAdviser: false,
  aqParaplanner: false,
};

/** The flags exactly as the row holds them, before any derivation. */
export function recordedFlags(record: Al_outcomes): AccountabilityFlags {
  return {
    fqAdviser: record.al_fqadviseraccountable === true,
    fqParaplanner: record.al_fqparaplanneraccountable === true,
    aqAdviser: record.al_aqadviseraccountable === true,
    aqParaplanner: record.al_aqparaplanneraccountable === true,
  };
}

/** Whether anyone has recorded a judgement on this row at all. */
export function isRecorded(flags: AccountabilityFlags): boolean {
  return flags.fqAdviser || flags.fqParaplanner || flags.aqAdviser || flags.aqParaplanner;
}

/**
 * What the export would derive for an untouched row.
 *
 * The adviser does not carry the file and the paraplanner does not carry the advice, so
 * neither is derived - a judgement can still name them, which is what this screen is for.
 */
export function derivedFlags(
  fileQualityFailed: boolean,
  adviceQualityFailed: boolean,
): AccountabilityFlags {
  return {
    fqAdviser: false,
    fqParaplanner: fileQualityFailed,
    aqAdviser: adviceQualityFailed,
    aqParaplanner: false,
  };
}

/**
 * The flags the export will actually use, and where they came from.
 *
 * Shown rather than the raw row, because a row of four falses exports as the derived pair
 * and a screen drawing four empty boxes over it would be telling the reader the opposite of
 * what the extract says.
 */
export function effectiveAccountability(
  recorded: AccountabilityFlags,
  fileQualityFailed: boolean,
  adviceQualityFailed: boolean,
): { flags: AccountabilityFlags; source: AccountabilitySource } {
  if (isRecorded(recorded)) {
    return { flags: recorded, source: 'recorded' };
  }

  return { flags: derivedFlags(fileQualityFailed, adviceQualityFailed), source: 'derived' };
}

/**
 * Whether the outcome is one that demands remediation, which is what makes an Advice
 * Quality fail (OutcomeRules.RequiresRemediation: anything but a Pass).
 *
 * Read from the label rather than the option value because that is what the outcome hook
 * already resolved; the values live in the generated map and both spellings of "Pass" the
 * scale carries - Pass, and Pass with issues - are distinguished here, since only the bare
 * Pass is clean.
 */
export function adviceQualityFailedFrom(outcomeLabel: string | null): boolean {
  if (outcomeLabel === null) return false;
  const normalised = outcomeLabel.trim().toLowerCase();
  if (normalised === '' || normalised === '—') return false;
  return normalised !== 'pass';
}
