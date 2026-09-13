import { inForceOn } from '../../lib/effectiveWindow';
import type { Al_questionversions } from '../../generated/models/Al_questionversionsModel';

/**
 * The generated model types an empty column as undefined, but the SDK returns it as null,
 * which new Date() reads as the epoch rather than as an invalid date. Both are "not set".
 */
type EffectiveDates = {
  [K in 'al_effectivefrom' | 'al_effectiveto']?: Al_questionversions[K] | null;
};

/**
 * Whether a question version is in force on a day (AD-015, BR-013). Mirrors
 * ResponseRules.IsVersionEffective in plugins/OutcomeTesting.Plugins, which is authoritative:
 * in force from the start of its effective-from day until the start of its effective-to day.
 * RetireAndSucceedQuestion stamps both with the same date, so on the retire day the successor
 * alone is current. A retired version stays Active and keeps its answers; it is simply not the
 * version a review is read against.
 *
 * The window itself lives in `lib/effectiveWindow`, because the Question library applies the
 * same rule to sections and two copies of it would be two things to keep in step.
 */
export function isVersionEffective(version: EffectiveDates, asOf: Date): boolean {
  return inForceOn(version.al_effectivefrom, version.al_effectiveto, asOf);
}
