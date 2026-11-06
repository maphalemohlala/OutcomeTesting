/**
 * The effective-date window, in one place (AD-015, AD-091, AD-123).
 *
 * Mirrors `ResponseRules.IsVersionEffective` in `plugins/OutcomeTesting.Plugins`, which is
 * authoritative. Sections and question versions use the same window, and the plug-in
 * assembly says so structurally: `SectionRules.IsSectionEffective` delegates to
 * `ResponseRules.IsVersionEffective` outright, with the comment "so the two can never
 * diverge". This module is the client half of that arrangement: the Question library reads
 * it directly, and `versionEffective.isVersionEffective` is a thin delegation to it rather
 * than a second copy of the rule.
 */

/** The UTC day a date-only column or a timestamp falls on, or null when it is not set. */
export function dayOf(value: string | Date | null | undefined): number | null {
  if (value === undefined || value === null) return null;
  const parsed = value instanceof Date ? value : new Date(value);
  const time = parsed.getTime();
  if (Number.isNaN(time)) return null;
  return Date.UTC(parsed.getUTCFullYear(), parsed.getUTCMonth(), parsed.getUTCDate());
}

/** Dated out on or before `asOf`. Only this is retired: a date yet to come is not. */
export function hasEnded(effectiveTo: string | Date | null | undefined, asOf: Date): boolean {
  const to = dayOf(effectiveTo);
  const day = dayOf(asOf);
  return to !== null && day !== null && to <= day;
}

/** Dated to start after `asOf`: not offered yet, which is not the same as retired. */
export function notStarted(effectiveFrom: string | Date | null | undefined, asOf: Date): boolean {
  const from = dayOf(effectiveFrom);
  const day = dayOf(asOf);
  return from !== null && day !== null && from > day;
}

/**
 * In force from the start of the effective-from day until the start of the effective-to
 * day. RetireAndSucceedQuestion stamps both with the same date, so on the changeover day
 * the successor alone is current. A dated-out row stays Active and keeps its answers; it is
 * simply no longer asked.
 */
export function inForceOn(
  effectiveFrom: string | Date | null | undefined,
  effectiveTo: string | Date | null | undefined,
  asOf: Date,
): boolean {
  const day = dayOf(asOf);
  if (day === null) return true;

  const from = dayOf(effectiveFrom);
  if (from !== null && from > day) return false;

  const to = dayOf(effectiveTo);
  if (to !== null && to <= day) return false;

  return true;
}
