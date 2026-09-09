import type { Al_questionversions } from '../../generated/models/Al_questionversionsModel';

/**
 * The generated model types an empty column as undefined, but the SDK returns it as null,
 * which new Date() reads as the epoch rather than as an invalid date. Both are "not set".
 */
type EffectiveDates = {
  [K in 'al_effectivefrom' | 'al_effectiveto']?: Al_questionversions[K] | null;
};

/** The UTC day a date-only column or a timestamp falls on, or null when it is not set. */
function dayOf(value: string | Date | null | undefined): number | null {
  if (value === undefined || value === null) return null;
  const parsed = value instanceof Date ? value : new Date(value);
  const time = parsed.getTime();
  if (Number.isNaN(time)) return null;
  return Date.UTC(parsed.getUTCFullYear(), parsed.getUTCMonth(), parsed.getUTCDate());
}

/**
 * Whether a question version is in force on a day (AD-015, BR-013). Mirrors
 * ResponseRules.IsVersionEffective in plugins/OutcomeTesting.Plugins, which is authoritative:
 * in force from the start of its effective-from day until the start of its effective-to day.
 * RetireAndSucceedQuestion stamps both with the same date, so on the retire day the successor
 * alone is current. A retired version stays Active and keeps its answers; it is simply not the
 * version a review is read against.
 */
export function isVersionEffective(version: EffectiveDates, asOf: Date): boolean {
  const day = dayOf(asOf);
  if (day === null) return true;

  const from = dayOf(version.al_effectivefrom);
  if (from !== null && from > day) return false;

  const to = dayOf(version.al_effectiveto);
  if (to !== null && to <= day) return false;

  return true;
}
